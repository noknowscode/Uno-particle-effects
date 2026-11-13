using Microsoft.UI.Xaml;
using SkiaSharp;

namespace Uno.Effects.Particles;

// The Procedural family: the picture is computed per pixel or per cell rather
// than simulated per particle. Two of these are SKSL shaders; the rest are
// cheap enough to stay on the CPU.

/// <summary>
/// Four interfering sine fields, drawn by an SKSL shader. Continuous rather
/// than the grid of cells the study sheet used, and the same cost at any size.
/// </summary>
[Effect("Plasma", EffectCategory.Procedural,
	Cost = EffectCost.Medium,
	Description = "Four interfering sine fields, drawn by a shader.",
	Tags = "shader,generative,background",
	IsShaderBased = true,
	CommonKnobs = "Speed,Opacity")]
public sealed partial class Plasma : ShaderEffect
{
	/// <summary>
	/// Grid the field is sampled on. 28 is the resolution the study sheet used;
	/// 0 evaluates continuously, which the shader can do and the original grid
	/// could not.
	/// </summary>
	[EffectProperty(0, 96, Step = 1, Group = "Plasma")]
	public double Cells
	{
		get => (double)GetValue(CellsProperty);
		set => SetValue(CellsProperty, value);
	}

	public static readonly DependencyProperty CellsProperty = DependencyProperty.Register(
		nameof(Cells), typeof(double), typeof(Plasma), new PropertyMetadata(28d, OnVisualChanged));

	protected override string ShaderResourceName => "Uno.Effects.Particles.Shaders.Plasma.sksl";

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void SetUniforms(SKRuntimeEffectUniforms uniforms, in EffectFrame frame)
	{
		uniforms["uResolution"] = new SKSize(frame.Width, frame.Height);
		uniforms["uTime"] = frame.Time;
		uniforms["uCells"] = (float)Cells;
		SetColor(uniforms, "uColor", Accent, (float)Opacity);
	}
}

/// <summary>
/// Blobs that merge as they pass, drawn by an SKSL shader evaluating an
/// inverse-square field. The orbits are simulated in C#; only the field is
/// shaded.
/// </summary>
[Effect("Metaballs", EffectCategory.Procedural,
	Cost = EffectCost.Heavy,
	Description = "Blobs that merge as they pass, shaded from a scalar field.",
	Tags = "shader,organic,hero",
	IsShaderBased = true,
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Metaballs : ShaderEffect
{
	/// <summary>Matches the array length declared in Metaballs.sksl.</summary>
	private const int MaxBalls = 8;

	private struct Ball
	{
		public float Phase;
		public float Rate;
		public float Radius;
	}

	private readonly float[] _uniformBalls = new float[MaxBalls * 3];
	private Ball[] _balls = [];

	/// <summary>
	/// Field value at which the surface becomes solid. Lower values fatten the
	/// blobs and make them merge sooner.
	/// </summary>
	[EffectProperty(0.4, 2.5, Step = 0.05, Group = "Metaballs")]
	public double Threshold
	{
		get => (double)GetValue(ThresholdProperty);
		set => SetValue(ThresholdProperty, value);
	}

	public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
		nameof(Threshold), typeof(double), typeof(Metaballs), new PropertyMetadata(1d, OnVisualChanged));

	protected override string ShaderResourceName => "Uno.Effects.Particles.Shaders.Metaballs.sksl";

	protected override int NaturalCount => 5;

	protected override void Emit(in EffectFrame frame)
	{
		var count = Math.Min(MaxBalls, ResolveCount(in frame));
		_balls = new Ball[count];

		for (var i = 0; i < count; i++)
		{
			_balls[i] = new Ball
			{
				Phase = Random.Next(Tau),
				Rate = Random.Next(0.4f) + 0.3f,
				Radius = Random.Next(10f) + 14f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void SetUniforms(SKRuntimeEffectUniforms uniforms, in EffectFrame frame)
	{
		// The study sheet was tuned on a ~150px tile; keeping the blobs
		// proportional means the effect reads the same in a grid cell and on a
		// full-page preview.
		var scale = MathF.Min(frame.Width, frame.Height) / 150f * ResolveSize();

		Array.Clear(_uniformBalls);

		for (var i = 0; i < _balls.Length; i++)
		{
			var ball = _balls[i];
			_uniformBalls[i * 3] = (frame.Width / 2f)
				+ (MathF.Sin((frame.Time * ball.Rate) + ball.Phase) * frame.Width * 0.28f);
			_uniformBalls[(i * 3) + 1] = (frame.Height / 2f)
				+ (MathF.Cos((frame.Time * ball.Rate * 1.3f) + ball.Phase) * frame.Height * 0.28f);
			_uniformBalls[(i * 3) + 2] = ball.Radius * scale;
		}

		uniforms["uResolution"] = new SKSize(frame.Width, frame.Height);
		uniforms["uBalls"] = _uniformBalls;
		uniforms["uCount"] = (float)_balls.Length;
		uniforms["uThreshold"] = (float)Threshold;
		SetColor(uniforms, "uColor", Primary, (float)Opacity);
	}
}

/// <summary>Film grain: a small noise texture regenerated every frame and upscaled.</summary>
[Effect("Grain", EffectCategory.Procedural,
	Cost = EffectCost.Medium,
	Description = "Film grain, regenerated every frame.",
	Tags = "texture,overlay,analogue",
	CommonKnobs = "Speed,Opacity,Seed")]
public sealed partial class Grain : ParticleEffect
{
	/// <summary>
	/// The texture is deliberately tiny and drawn without filtering: the chunky
	/// upscale is the look, and it costs 4096 pixels instead of the surface.
	/// </summary>
	private const int Resolution = 64;

	private readonly byte[] _pixels = new byte[Resolution * Resolution * 4];

	private static readonly SKImageInfo Info =
		new(Resolution, Resolution, SKColorType.Rgba8888, SKAlphaType.Opaque);

	protected override int NaturalCount => 0;

	protected override void Emit(in EffectFrame frame)
	{
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _pixels.Length; i += 4)
		{
			var value = (byte)(42f + (Random.NextSingle() * 90f));
			_pixels[i] = value;
			_pixels[i + 1] = value;
			_pixels[i + 2] = (byte)(value - 3);
			_pixels[i + 3] = 255;
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		using var image = SKImage.FromPixelCopy(Info, _pixels);

		var paint = Paint;
		paint.Style = SKPaintStyle.Fill;
		paint.Color = SKColors.White.WithAlpha(Alpha(1f));

		canvas.DrawImage(image,
			SKRect.Create(0, 0, frame.Width, frame.Height),
			new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
			paint);
	}
}

/// <summary>Concentric rings of dots swelling and shrinking as a wave passes.</summary>
[Effect("Halftone", EffectCategory.Procedural,
	Cost = EffectCost.Medium,
	Description = "Rings of dots swelling as a wave passes through them.",
	Tags = "grid,print,graphic",
	CommonKnobs = "Count,Speed,Opacity")]
public sealed partial class Halftone : ParticleEffect
{
	protected override int NaturalCount => 14;

	protected override void Emit(in EffectFrame frame)
	{
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var columns = ResolveGridColumns(in frame);
		var cell = frame.Width / columns;
		var rows = GridRows(cell, in frame);
		var centreX = frame.Width / 2f;
		var centreY = frame.Height / 2f;

		for (var i = 0; i < columns; i++)
		{
			for (var j = 0; j < rows; j++)
			{
				var x = (i + 0.5f) * cell;
				var y = (j + 0.5f) * cell;
				var distance = MathF.Sqrt(((x - centreX) * (x - centreX)) + ((y - centreY) * (y - centreY)));
				var wave = 0.5f + (0.5f * MathF.Sin((distance * 0.15f) - (frame.Time * 3f)));

				Dot(canvas, x, y, wave * cell * 0.45f, Primary, 0.85f);
			}
		}
	}
}

/// <summary>Curtains of light rising from a noise-driven baseline.</summary>
[Effect("Aurora", EffectCategory.Procedural,
	Cost = EffectCost.Medium,
	Description = "Curtains of light rising from a noise baseline.",
	Tags = "sky,colour,soft",
	CommonKnobs = "Speed,Opacity")]
public sealed partial class Aurora : ParticleEffect
{
	protected override int NaturalCount => 0;

	protected override void Emit(in EffectFrame frame)
	{
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		// One stroke every two pixels: fine enough to read as a field, coarse
		// enough that the whole thing is a few hundred lines.
		var count = Math.Max(1, (int)MathF.Round(frame.Width / 2f));
		var colour = PaletteAt(2);

		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 2f;

		for (var i = 0; i < count; i++)
		{
			var x = (i * 2f) + 1f;
			var u = i / (float)count;

			var height = (Noise.Sample((u * 3f) + (frame.Time * 0.15f), frame.Time * 0.1f) * frame.Height * 0.5f)
				+ (frame.Height * 0.1f);
			var top = (frame.Height * 0.55f) - (Noise.Sample(u * 2f, frame.Time * 0.07f) * frame.Height * 0.3f);

			paint.Color = WithAlpha(colour, 0.15f + (0.35f * Noise.Sample(u * 5f, frame.Time * 0.3f)));
			canvas.DrawLine(x, top, x, top + height, paint);
		}
	}
}

/// <summary>A grid of points jittered by noise, so the whole field glitters.</summary>
[Effect("Shimmer", EffectCategory.Procedural,
	Cost = EffectCost.Medium,
	Description = "A grid of points jittered by noise.",
	Tags = "grid,texture,calm",
	CommonKnobs = "Count,Speed,Size,Opacity")]
public sealed partial class Shimmer : ParticleEffect
{
	protected override int NaturalCount => 16;

	protected override void Emit(in EffectFrame frame)
	{
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var columns = ResolveGridColumns(in frame);
		var cell = frame.Width / columns;
		var rows = GridRows(cell, in frame);
		var size = ResolveSize();

		for (var j = 0; j < rows; j++)
		{
			for (var i = 0; i < columns; i++)
			{
				// Horizontal jitter is more than twice the vertical, which is
				// what gives the field its sideways glitter.
				var jitterX = (Noise.Sample(i * 0.3f, (j * 0.3f) + (frame.Time * 1.5f)) - 0.5f) * cell * 1.5f;
				var jitterY = (Noise.Sample((i * 0.3f) + 7f, (j * 0.3f) + (frame.Time * 1.5f)) - 0.5f) * cell * 0.6f;

				Dot(canvas, ((i + 0.5f) * cell) + jitterX, ((j + 0.5f) * cell) + jitterY,
					0.9f * size, Primary, 0.7f);
			}
		}
	}
}

/// <summary>Television static: half the cells lit, redrawn every frame.</summary>
[Effect("Static", EffectCategory.Procedural,
	Cost = EffectCost.Medium,
	Description = "Television static, redrawn every frame.",
	Tags = "texture,analogue,noise",
	CommonKnobs = "Count,Speed,Opacity,Seed")]
public sealed partial class Static : ParticleEffect
{
	private float[] _cells = [];
	private int _columns;
	private int _rows;

	protected override int NaturalCount => 24;

	protected override void Emit(in EffectFrame frame)
	{
		_columns = ResolveGridColumns(in frame);
		_rows = GridRows(frame.Width / _columns, in frame);
		_cells = new float[_columns * _rows];
	}

	protected override void Step(in EffectFrame frame)
	{
		// Generated here rather than while drawing, so a frame is drawn from
		// state that already exists and the same frame renders the same twice.
		for (var i = 0; i < _cells.Length; i++)
		{
			_cells[i] = Random.NextSingle();
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		if (_columns == 0)
		{
			return;
		}

		var cell = frame.Width / _columns;

		for (var i = 0; i < _columns; i++)
		{
			for (var j = 0; j < _rows; j++)
			{
				var value = _cells[(i * _rows) + j];

				if (value > 0.5f)
				{
					// The half-cell overlap hides seams between neighbours.
					Rect(canvas, i * cell, j * cell, cell + 0.5f, cell + 0.5f,
						Primary, (value - 0.5f) * 1.2f);
				}
			}
		}
	}
}

/// <summary>A grid of dots breathing on a slow noise field.</summary>
[Effect("Pulse grid", EffectCategory.Procedural,
	Cost = EffectCost.Medium,
	Description = "A grid of dots breathing on a slow noise field.",
	Tags = "grid,calm,texture",
	CommonKnobs = "Count,Speed,Opacity")]
public sealed partial class PulseGrid : ParticleEffect
{
	protected override int NaturalCount => 12;

	protected override void Emit(in EffectFrame frame)
	{
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var columns = ResolveGridColumns(in frame);
		var cell = frame.Width / columns;
		var rows = GridRows(cell, in frame);

		for (var i = 0; i < columns; i++)
		{
			for (var j = 0; j < rows; j++)
			{
				var value = Noise.Sample((i * 0.25f) + (frame.Time * 0.5f), j * 0.25f);
				Dot(canvas, (i + 0.5f) * cell, (j + 0.5f) * cell,
					value * cell * 0.45f, Primary, 0.3f + (value * 0.6f));
			}
		}
	}
}

/// <summary>A single soft mass whose outline is deformed by noise.</summary>
[Effect("Blob", EffectCategory.Procedural,
	Cost = EffectCost.Light,
	Description = "A single soft mass whose outline is deformed by noise.",
	Tags = "organic,minimal,hero",
	CommonKnobs = "Speed,Size,Opacity")]
public sealed partial class Blob : ParticleEffect
{
	private const int Segments = 64;

	protected override int NaturalCount => 0;

	protected override void Emit(in EffectFrame frame)
	{
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centreX = frame.Width / 2f;
		var centreY = frame.Height / 2f;
		var extent = MathF.Min(frame.Width, frame.Height);
		var baseRadius = extent * 0.22f * ResolveSize();
		var wobble = extent * 0.16f;

		var builder = ResetPathBuilder();

		for (var i = 0; i <= Segments; i++)
		{
			var angle = i / (float)Segments * Tau;

			// Sampling the noise on a circle rather than by angle keeps the
			// outline continuous where it closes.
			var radius = baseRadius + ((Noise.Sample(
				(MathF.Cos(angle) * 1.2f) + 3f,
				(MathF.Sin(angle) * 1.2f) + (frame.Time * 0.5f)) - 0.5f) * wobble);

			var x = centreX + (MathF.Cos(angle) * radius);
			var y = centreY + (MathF.Sin(angle) * radius);

			if (i == 0)
			{
				builder.MoveTo(x, y);
			}
			else
			{
				builder.LineTo(x, y);
			}
		}

		builder.Close();
		using var path = builder.Detach();

		var paint = Paint;
		paint.Style = SKPaintStyle.Fill;
		paint.Color = WithAlpha(Primary, 0.9f);
		canvas.DrawPath(path, paint);
	}
}
