using System;
using System.Collections.Generic;
using System.Linq;
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
        private record struct ParentAndBreakElement(UIElement Parent, UIElement BreakElement);



        private readonly Stack<ParentAndBreakElement> _parentStack = new();

        protected override Geometry GetLayoutClip(Size layoutSlotSize)
        {
            return new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            InvalidateVisual();

            return base.ArrangeOverride(finalSize);
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
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var parent = Parent as UIElement;
            var breakElement = this as UIElement;
            while (parent is { })
            {
                _parentStack.Push(new ParentAndBreakElement(parent, breakElement));

                breakElement = parent;
                parent = VisualTreeHelper.GetParent(parent) as UIElement;
            }

            while (_parentStack.TryPop(out var current))
            {
                if (current.Parent.RenderSize.Width != 0 &&
                    current.Parent.RenderSize.Height != 0)
                {
                    var clonePresenter = new ClonePresenter()
                    {
                        TargetElement = current.Parent,
                    };

                    DrawUIElement(drawingContext, clonePresenter, current.Parent.TranslatePoint(default, this), current.Parent.RenderSize);
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
        }
    }
}
