using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using BlurBehindTest.Internals;

namespace BlurBehindTest
{
    public class BackgroundPresenter : FrameworkElement
    {
        private static readonly Type _typeRenderDataDrawingContext = typeof(DrawingContext).Assembly
            .GetType("System.Windows.Media.RenderDataDrawingContext", true)!;

        private static readonly FieldInfo _drawingContentOfUIElement = typeof(UIElement)
            .GetField("_drawingContent", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo _renderDataOfVisualDrawingContext = _typeRenderDataDrawingContext
            .GetField("_renderData", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly Func<UIElement, DrawingContext> _renderOpenMethod = typeof(UIElement)
            .GetMethod("RenderOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<UIElement, DrawingContext>>();

        private static readonly Action<UIElement, DrawingContext> _onRenderMethod = typeof(UIElement)
            .GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<UIElement, DrawingContext>>();

        private readonly Stack<UIElement> _parentStack = new();

        private static void ForceRender(UIElement target)
        {
            using DrawingContext drawingContext = _renderOpenMethod(target);

            _onRenderMethod.Invoke(target, drawingContext);
        }

        private static void DrawVisual(DrawingContext drawingContext, Visual visual, Point relatedXY, Size size)
        {
            drawingContext.DrawRectangle(
                new VisualBrush(visual)
                {
                    AutoLayoutContent = false,
                }, null,
                new Rect(relatedXY.X, relatedXY.Y, size.Width, size.Height));
        }

        private static void DrawUIElement(DrawingContext drawingContext, UIElement self, UIElement element)
        {
            var relatedXY = element.TranslatePoint(default, self);

            DrawVisual(drawingContext, element, relatedXY, element.RenderSize);
        }

        protected override Geometry GetLayoutClip(Size layoutSlotSize)
        {
            return new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        }

        protected override void ParentLayoutInvalidated(UIElement child)
        {
            base.ParentLayoutInvalidated(child);
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }

        protected override void OnVisualParentChanged(DependencyObject oldParentObject)
        {
            if (oldParentObject is UIElement oldParent)
            {
                oldParent.LayoutUpdated -= ParentLayoutUpdated;
            }

            if (Parent is UIElement newParent)
            {
                newParent.LayoutUpdated += ParentLayoutUpdated;
            }
        }

        private void ParentLayoutUpdated(object? sender, EventArgs e)
        {
            // cannot use 'InvalidateVisual' here, because it will cause infinite loop

            ForceRender(this);
        }

        private static void DrawBackground(
            DrawingContext drawingContext, UIElement self, 
            Stack<UIElement> parentStackStorage, 
            bool throwExceptionIfParentArranging)
        {
            var parent = VisualTreeHelper.GetParent(self) as UIElement;
            while (parent is { })
            {
                // parent not visible, no need to render
                if (!parent.IsVisible)
                {
                    parentStackStorage.Clear();
                    return;
                }

                // is parent arranging
                // we cannot render it
                if (parent.RenderSize.Width == 0 ||
                    parent.RenderSize.Height == 0)
                {
                    parentStackStorage.Clear();

                    if (throwExceptionIfParentArranging)
                    {
                        throw new InvalidOperationException("Arranging");
                    }

                    // render after parent arranging finished
                    self.InvalidateArrange();
                    return;
                }

                parentStackStorage.Push(parent);

                parent = VisualTreeHelper.GetParent(parent) as UIElement;
            }

            while (parentStackStorage.TryPop(out var currentParent))
            {
                if (!parentStackStorage.TryPeek(out var breakElement))
                {
                    breakElement = self;
                }

                var parentRelatedXY = currentParent.TranslatePoint(default, self);

                // has render data
                if (_drawingContentOfUIElement.GetValue(currentParent) is { } parentDrawingContent)
                {
                    var drawingVisual = new DrawingVisual();
                    var drawingVisualRenderContext = drawingVisual.RenderOpen();
                    _renderDataOfVisualDrawingContext.SetValue(drawingVisualRenderContext, parentDrawingContent);
                    drawingVisualRenderContext.Close();

                    DrawVisual(drawingContext, drawingVisual, parentRelatedXY, currentParent.RenderSize);
                }

                if (currentParent is Panel parentPanelToRender)
                {
                    foreach (UIElement child in parentPanelToRender.Children)
                    {
                        if (child == breakElement)
                        {
                            break;
                        }

                        if (child.IsVisible)
                        {
                            DrawUIElement(drawingContext, self, child);
                        }
                    }
                }
            }
        }

        public static void DrawBackground(DrawingContext drawingContext, UIElement self)
        {
            var parentStack = new Stack<UIElement>();
            DrawBackground(drawingContext, self, parentStack, true);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            DrawBackground(drawingContext, this, _parentStack, false);
        }
    }
}
