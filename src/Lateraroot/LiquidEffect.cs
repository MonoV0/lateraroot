using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace Lateraroot;

internal sealed class LiquidEffect : ShaderEffect
{
    private static readonly PixelShader shader = new() { UriSource = new Uri("/Lateraroot;component/Assets/Glass.ps", UriKind.Relative) };
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty MappingProperty = DependencyProperty.Register(nameof(Mapping), typeof(Point4D), typeof(LiquidEffect), new UIPropertyMetadata(new Point4D(0, 0, 1, 1), PixelShaderConstantCallback(0)));
    public Point4D Mapping { get => (Point4D)GetValue(MappingProperty); set => SetValue(MappingProperty, value); }
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(Point4D), typeof(LiquidEffect), new UIPropertyMetadata(new Point4D(88, 88, 16, 9), PixelShaderConstantCallback(1)));
    public Point4D Shape { get => (Point4D)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Point4D), typeof(LiquidEffect), new UIPropertyMetadata(new Point4D(1, 1, 1, 1), PixelShaderConstantCallback(2)));
    public Point4D Tint { get => (Point4D)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(nameof(Light), typeof(Point4D), typeof(LiquidEffect), new UIPropertyMetadata(new Point4D(.12, .08, 2.7, 1), PixelShaderConstantCallback(3)));
    public Point4D Light { get => (Point4D)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    public LiquidEffect()
    {
        PixelShader = shader;
        UpdateShaderValue(InputProperty); UpdateShaderValue(MappingProperty); UpdateShaderValue(ShapeProperty); UpdateShaderValue(TintProperty); UpdateShaderValue(LightProperty);
    }
}
