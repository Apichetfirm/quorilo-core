using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CreditMonitor
{
    public sealed class HorizontalMotionBlurEffect : ShaderEffect
    {
        private static readonly PixelShader shader = LoadShader();
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(HorizontalMotionBlurEffect), 0, SamplingMode.Bilinear);
        // Radius is expressed as a fraction of the rendered surface width.
        public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register("Radius", typeof(double), typeof(HorizontalMotionBlurEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));
        public Brush Input { get { return (Brush)GetValue(InputProperty); } set { SetValue(InputProperty, value); } }
        public double Radius { get { return (double)GetValue(RadiusProperty); } set { SetValue(RadiusProperty, value); } }

        public HorizontalMotionBlurEffect()
        {
            PixelShader = shader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(RadiusProperty);
        }
        private static PixelShader LoadShader()
        {
            var result = new PixelShader { ShaderRenderMode = ShaderRenderMode.Auto };
            using (Stream stream = typeof(HorizontalMotionBlurEffect).Assembly.GetManifestResourceStream("HorizontalMotionBlur.ps")) {
                if (stream == null) throw new InvalidOperationException("Missing motion shader resource.");
                result.SetStreamSource(stream);
            }
            result.Freeze();
            return result;
        }
    }
}
