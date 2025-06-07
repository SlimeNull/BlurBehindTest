using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        private static readonly FieldInfo _drawingContentOfUIElement = typeof(UIElement)
            .GetField("_drawingContent", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo _contentOfDrawingVisual = typeof(DrawingVisual)
            .GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly Func<UIElement, DrawingContext> _renderOpenMethod = typeof(UIElement)
            .GetMethod("RenderOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<UIElement, DrawingContext>>();

        private static readonly Action<UIElement, DrawingContext> _onRenderMethod = typeof(UIElement)
            .GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<UIElement, DrawingContext>>();

        private static readonly GetContentBoundsDelegate _methodGetContentBounds = typeof(VisualBrush)
            .GetMethod("GetContentBounds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<GetContentBoundsDelegate>();

        private delegate void GetContentBoundsDelegate(VisualBrush visualBrush, out Rect bounds);
        private readonly Stack<UIElement> _parentStack = new();

        private static void ForceRender(UIElement target)
        {
            using DrawingContext drawingContext = _renderOpenMethod(target);

            _onRenderMethod.Invoke(target, drawingContext);
        }

        private static void DrawVisual(DrawingContext drawingContext, Visual visual, Point relatedXY)
        {
            var visualBrush = new VisualBrush(visual);
            _methodGetContentBounds.Invoke(visualBrush, out var contentBounds);

            drawingContext.DrawRectangle(
                visualBrush, null,
                new Rect(relatedXY.X + contentBounds.X, contentBounds.Y, contentBounds.Width, contentBounds.Height));
        }

        protected override Geometry GetLayoutClip(Size layoutSlotSize)
        {
            return new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
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

            Debug.WriteLine("Parent layout updated, forcing render of BackgroundPresenter.");
        }

        private static void DrawBackground(
            DrawingContext drawingContext, UIElement self,
            Stack<UIElement> parentStackStorage,
            int maxDepth,
            bool throwExceptionIfParentArranging)
        {
#if DEBUG
            bool selfInDesignMode = DesignerProperties.GetIsInDesignMode(self);
#endif

            var parent = VisualTreeHelper.GetParent(self) as UIElement;
            while (
                parent is { } &&
                parentStackStorage.Count < maxDepth)
            {
                // parent not visible, no need to render
                if (!parent.IsVisible)
                {
                    parentStackStorage.Clear();
                    return;
                }

#if DEBUG
                if (selfInDesignMode &&
                    parent.GetType().ToString().Contains("VisualStudio"))
                {
                    // 遍历到 VS 自身的设计器元素, 中断!
                    break;
                }
#endif

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

            var selfRect = new Rect(0, 0, self.RenderSize.Width, self.RenderSize.Height);
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
                    _contentOfDrawingVisual.SetValue(drawingVisual, parentDrawingContent);

                    DrawVisual(drawingContext, drawingVisual, parentRelatedXY);
                }

                if (currentParent is Panel parentPanelToRender)
                {
                    foreach (UIElement child in parentPanelToRender.Children)
                    {
                        if (child == breakElement)
                        {
                            break;
                        }

                        var childRelatedXY = child.TranslatePoint(default, self);
                        var childRect = new Rect(childRelatedXY, child.RenderSize);

                        if (!selfRect.IntersectsWith(childRect))
                        {
                            continue; // skip if not intersecting
                        }

                        if (child.IsVisible)
                        {
                            DrawVisual(drawingContext, child, childRelatedXY);
                        }
                    }
                }
            }
        }

        public static void DrawBackground(DrawingContext drawingContext, UIElement self)
        {
            var parentStack = new Stack<UIElement>();
            DrawBackground(drawingContext, self, parentStack, int.MaxValue, true);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            DrawBackground(drawingContext, this, _parentStack, MaxDepth, false);
        }

        public int MaxDepth
        {
            get { return (int)GetValue(MaxDepthProperty); }
            set { SetValue(MaxDepthProperty, value); }
        }

        public static readonly DependencyProperty MaxDepthProperty =
            DependencyProperty.Register("MaxDepth", typeof(int), typeof(BackgroundPresenter), new PropertyMetadata(64));
    }
}
