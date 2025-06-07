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

        private static void EnsureRendered(UIElement target)
        {
            if (_drawingContentOfUIElement.GetValue(target) is null)
            {
                ForceRender(target);
            }
        }

        private void DrawUIElement(DrawingContext drawingContext, UIElement element, Point relatedXY, Size size)
        {
            drawingContext.DrawRectangle(
                new VisualBrush(element), null,
                new Rect(relatedXY.X, relatedXY.Y, size.Width, size.Height));
        }

        private void DrawUIElement(DrawingContext drawingContext, UIElement element)
        {
            var relatedXY = element.TranslatePoint(default, this);

            DrawUIElement(drawingContext, element, relatedXY, element.RenderSize);
        }

        protected override Geometry GetLayoutClip(Size layoutSlotSize)
        {
            return new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        }

        protected override void ParentLayoutInvalidated(UIElement child)
        {
            base.ParentLayoutInvalidated(child);
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
                EnsureRendered(current.Parent);

                if (_drawingContentOfUIElement.GetValue(current.Parent) is { } parentDrawingContent)
                {
                    var renderDataPresenter = new RenderDataPresenter(current.Parent.RenderSize, parentDrawingContent);
                    DrawUIElement(drawingContext, renderDataPresenter, current.Parent.TranslatePoint(default, this), current.Parent.RenderSize);
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



        private int RenderVersion
        {
            get { return (int)GetValue(RenderVersionPropertyKey.DependencyProperty); }
            set { SetValue(RenderVersionPropertyKey, value); }
        }

        // Using a DependencyProperty as the backing store for Version.  This enables animation, styling, binding, etc...
        private static readonly DependencyPropertyKey RenderVersionPropertyKey =
            DependencyProperty.RegisterReadOnly("RenderVersion", typeof(int), typeof(BackgroundPresenter), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));



        private class RenderDataPresenter : UIElement
        {
            private static readonly Type _typeRenderDataDrawingContext = typeof(DrawingContext).Assembly
                .GetType("System.Windows.Media.RenderDataDrawingContext", true)!;

            private static readonly FieldInfo _renderDataOfVisualDrawingContext = _typeRenderDataDrawingContext
                .GetField("_renderData", BindingFlags.Instance | BindingFlags.NonPublic)!;

            private static readonly Func<UIElement, DrawingContext> _renderOpenMethod = typeof(UIElement)
                .GetMethod("RenderOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<UIElement, DrawingContext>>();

            private static readonly Action<UIElement, DrawingContext> _onRenderMethod = typeof(UIElement)
                .GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<UIElement, DrawingContext>>();


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
                ReplaceRenderData(drawingContext, _renderData);
            }

            private static void ReplaceRenderData(DrawingContext renderDataDrawingContext, object? renderData)
            {
                _renderDataOfVisualDrawingContext.SetValue(renderDataDrawingContext, renderData);
            }
        }
    }
}
