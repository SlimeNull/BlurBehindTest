using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace BlurBehindTest
{
    public class ClonePresenter : FrameworkElement
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



        public UIElement TargetElement
        {
            get { return (UIElement)GetValue(TargetElementProperty); }
            set { SetValue(TargetElementProperty, value); }
        }

        // Using a DependencyProperty as the backing store for TargetElement.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TargetElementProperty =
            DependencyProperty.Register("TargetElement", typeof(UIElement), typeof(ClonePresenter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, propertyChangedCallback: OnTargetElementChanged));

        private static void OnTargetElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ClonePresenter presenter)
            {
                return;
            }

            if (e.OldValue is UIElement oldTarget)
            {
                oldTarget.LayoutUpdated -= presenter.TargetElementLayoutUpdated;
            }

            if (e.NewValue is UIElement newTarget)
            {
                newTarget.LayoutUpdated += presenter.TargetElementLayoutUpdated;
            }
        }

        private static void ReplaceRenderData(DrawingContext renderDataDrawingContext, object? renderData)
        {
            _renderDataOfVisualDrawingContext.SetValue(renderDataDrawingContext, renderData);
        }

        private static void EnsureRendered(UIElement target)
        {
            if (_drawingContentOfUIElement.GetValue(target) is null)
            {
                using DrawingContext drawingContext = _renderOpenMethod(target);

                _onRenderMethod.Invoke(target, drawingContext);
            }
        }

        private void TargetElementLayoutUpdated(object? sender, EventArgs e)
        {
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return TargetElement?.DesiredSize ?? default;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (TargetElement is null)
            {
                return;
            }

            EnsureRendered(TargetElement);
            var renderData = _drawingContentOfUIElement.GetValue(TargetElement);
            ReplaceRenderData(drawingContext, renderData);
        }
    }
}
