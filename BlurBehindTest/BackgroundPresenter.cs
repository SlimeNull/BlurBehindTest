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

        private record struct ParentAndBreakElement(UIElement Parent, UIElement BreakElement);

        private readonly Stack<ParentAndBreakElement> _parentStack = new();

        private object? _currentRenderData = null;

        private static void ForceRender(UIElement target)
        {
            using DrawingContext drawingContext = _renderOpenMethod(target);

            _onRenderMethod.Invoke(target, drawingContext);
        }

        private void DrawVisual(DrawingContext drawingContext, Visual visual, Point relatedXY, Size size)
        {
            drawingContext.DrawRectangle(
                new VisualBrush(visual)
                {
                    AutoLayoutContent = false,
                }, null,
                new Rect(relatedXY.X, relatedXY.Y, size.Width, size.Height));
        }

        private void DrawUIElement(DrawingContext drawingContext, UIElement element)
        {
            var relatedXY = element.TranslatePoint(default, this);

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
            ForceRender(this);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var parent = Parent as UIElement;
            var breakElement = this as UIElement;
            while (parent is { })
            {
                // is parent arranging
                if (parent.RenderSize.Width == 0 ||
                    parent.RenderSize.Height == 0)
                {
                    _parentStack.Clear();
                    _renderDataOfVisualDrawingContext.SetValue(drawingContext, _currentRenderData);
                    InvalidateArrange();
                    return;
                }

                _parentStack.Push(new ParentAndBreakElement(parent, breakElement));

                breakElement = parent;
                parent = VisualTreeHelper.GetParent(parent) as UIElement;
            }

            while (_parentStack.TryPop(out var current))
            {
                var parentRelatedXY = current.Parent.TranslatePoint(default, this);

                if (_drawingContentOfUIElement.GetValue(current.Parent) is { } parentDrawingContent)
                {
                    var drawingVisual = new DrawingVisual();
                    var drawingVisualRenderContext = drawingVisual.RenderOpen();

                    _onRenderMethod.Invoke(current.Parent, drawingVisualRenderContext);
                    drawingVisualRenderContext.Close();

                    DrawVisual(drawingContext, drawingVisual, parentRelatedXY, current.Parent.RenderSize);
                }

                if (current.Parent is Panel parentPanelToRender)
                {
                    foreach (UIElement child in parentPanelToRender.Children)
                    {
                        if (child == current.BreakElement)
                        {
                            break;
                        }

                        if (child != this && child.Visibility == Visibility.Visible)
                        {
                            DrawUIElement(drawingContext, child);
                        }
                    }
                }
            }

            _currentRenderData = _renderDataOfVisualDrawingContext.GetValue(drawingContext);
        }

        private class RenderDataPresenter : UIElement
        {
            private static readonly Type _typeRenderDataDrawingContext = typeof(DrawingContext).Assembly
                .GetType("System.Windows.Media.RenderDataDrawingContext", true)!;

            private static readonly FieldInfo _renderDataOfVisualDrawingContext = _typeRenderDataDrawingContext
                .GetField("_renderData", BindingFlags.Instance | BindingFlags.NonPublic)!;


            private readonly Size _size;
            private readonly object _renderData;

            public RenderDataPresenter(Size size, object renderData)
            {
                _size = size;
                _renderData = renderData;
            }

            protected override Size MeasureCore(Size availableSize)
            {
                return _size;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                _renderDataOfVisualDrawingContext.SetValue(drawingContext, _renderData);
            }
        }
    }
}
