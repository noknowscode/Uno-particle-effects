using SkiaSharp;

namespace Uno.Effects.Particles;

/// <summary>
/// The small drawing vocabulary every effect shares, ported from the study
/// sheet so the C# effects render the shapes the design was signed off on.
/// </summary>
public abstract partial class ParticleEffect
{
	/// <summary>Two pi.</summary>
	protected const float Tau = MathF.PI * 2f;

	private SKPaint? _paint;
	private SKPathBuilder? _pathBuilder;
	private float _spawnCarry;

	/// <summary>
	/// A single antialiased paint reused for the whole effect. Effects mutate
	/// its colour and style rather than allocating, which is what keeps the
	/// per-frame allocation budget at zero.
	/// </summary>
	protected SKPaint Paint => _paint ??= new SKPaint { IsAntialias = true };

	/// <summary>
	/// A builder to trace outlines into, reset and reused every frame. SkiaSharp 4
	/// paths are immutable: <see cref="SKPathBuilder.Detach"/> hands over the
	/// traced path, which the caller disposes after drawing, and leaves the
	/// builder empty for the next outline.
	/// </summary>
	protected SKPathBuilder ResetPathBuilder()
	{
		_pathBuilder ??= new SKPathBuilder();
		_pathBuilder.Reset();
		return _pathBuilder;
	}

	private protected void ResetSpawnCarry() => _spawnCarry = 0f;

	private protected void DisposeDrawingObjects()
	{
		_paint?.Dispose();
		_paint = null;
		_pathBuilder?.Dispose();
		_pathBuilder = null;
	}

	/// <summary>Fills a circle in the given colour at the given alpha.</summary>
	protected void Dot(SKCanvas canvas, float x, float y, float radius, SKColor color, float alpha)
	{
		var paint = Paint;
		paint.Style = SKPaintStyle.Fill;
		paint.Color = WithAlpha(color, alpha);
		canvas.DrawCircle(x, y, radius, paint);
	}

	/// <summary>A dot wrapped in two softer haloes, as used for light effects.</summary>
	protected void Glow(SKCanvas canvas, float x, float y, float radius, SKColor color, float alpha)
	{
		Dot(canvas, x, y, radius * 3.2f, color, alpha * 0.08f);
		Dot(canvas, x, y, radius * 1.9f, color, alpha * 0.2f);
		Dot(canvas, x, y, radius, color, alpha);
	}

	/// <summary>Strokes a line in the given colour at the given alpha.</summary>
	protected void Line(SKCanvas canvas, float x0, float y0, float x1, float y1, SKColor color, float alpha, float width = 1f)
	{
		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = width;
		paint.Color = WithAlpha(color, alpha);
		canvas.DrawLine(x0, y0, x1, y1, paint);
	}

	/// <summary>Fills an axis-aligned rectangle.</summary>
	protected void Rect(SKCanvas canvas, float x, float y, float width, float height, SKColor color, float alpha)
	{
		var paint = Paint;
		paint.Style = SKPaintStyle.Fill;
		paint.Color = WithAlpha(color, alpha);
		canvas.DrawRect(x, y, width, height, paint);
	}

	/// <summary>
	/// Rotates a velocity by <see cref="Direction"/>. Direction is an offset
	/// from the motion the effect was designed with, so 0 always means
	/// "as designed".
	/// </summary>
	protected void ApplyDirection(ref float vx, ref float vy)
	{
		if (Direction == 0)
		{
			return;
		}

		var angle = (float)(Direction * Math.PI / 180.0);
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		(vx, vy) = (vx * cos - vy * sin, vx * sin + vy * cos);
	}

	/// <summary>
	/// Draws a point cloud in unit space through the study sheet's rotating
	/// perspective projection: yaw tracks time, pitch breathes, and depth drives
	/// both the radius and the alpha so the far side reads as far.
	/// </summary>
	protected void Project(SKCanvas canvas, ReadOnlySpan<SKPoint3> points, in EffectFrame frame)
	{
		var yaw = frame.Time * 0.5f;
		var pitch = 0.4f + (MathF.Sin(frame.Time * 0.3f) * 0.3f);
		var cosYaw = MathF.Cos(yaw);
		var sinYaw = MathF.Sin(yaw);
		var cosPitch = MathF.Cos(pitch);
		var sinPitch = MathF.Sin(pitch);
		var size = ResolveSize();

		// The shorter side, so the shape stays inside a wide or tall frame.
		var extent = MathF.Min(frame.Width, frame.Height);

		foreach (var point in points)
		{
			var x = (point.X * cosYaw) + (point.Z * sinYaw);
			var z = (-point.X * sinYaw) + (point.Z * cosYaw);
			var y = (point.Y * cosPitch) - (z * sinPitch);
			z = (point.Y * sinPitch) + (z * cosPitch);

			var scale = 1f / (1.8f + z) * extent * 0.9f;
			var radius = (((1f - z) * 1.1f) + 0.4f) * size;
			var alpha = MathF.Min(1f, 0.35f + ((0.5f - z) * 0.6f));

			Dot(canvas, (frame.Width / 2f) + (x * scale), (frame.Height / 2f) + (y * scale), radius, Primary, alpha);
		}
	}

	/// <summary>
	/// The frame rate the effects were tuned at. Per-step constants (a damping
	/// factor, a spawn count) mean "per 1/60 s", and the helpers below rescale
	/// them to the actual frame time, so an effect looks the same at 30, 60 or
	/// 120 fps and at any <see cref="Speed"/>. At exactly 1/60 s they return
	/// the constant unchanged, so the fixed-timestep render tests are unaffected.
	/// </summary>
	protected const float TunedStepsPerSecond = 60f;

	/// <summary>How many tuned steps this frame's time amounts to.</summary>
	protected static float TunedSteps(in EffectFrame frame) => frame.DeltaTime * TunedStepsPerSecond;

	/// <summary>A per-step multiplier (such as velocity damping) applied over this frame.</summary>
	protected static float PerTunedStep(float factor, in EffectFrame frame) => MathF.Pow(factor, TunedSteps(in frame));

	/// <summary>
	/// Whole items to emit this frame for a rate given per tuned step. The
	/// fraction carries over, so a slow frame rate or a low speed still emits
	/// the same number per second.
	/// </summary>
	protected int SpawnThisFrame(float perStep, in EffectFrame frame)
	{
		_spawnCarry += perStep * TunedSteps(in frame);
		var spawn = (int)_spawnCarry;
		_spawnCarry -= spawn;
		return spawn;
	}

	/// <summary>
	/// Columns for a grid-based effect. The device tier scales by the square
	/// root so a busier device gets a finer grid rather than a stretched one,
	/// and <see cref="Count"/> overrides the designed value.
	/// </summary>
	protected int ResolveGridColumns(in EffectFrame frame) =>
		Math.Max(2, (int)MathF.Round((Count > 0 ? Count : NaturalCount) * MathF.Sqrt(frame.Density)));

	/// <summary>
	/// Rows needed to cover the frame with square cells of the given size. The
	/// study sheet ran on square tiles and used the column count for both axes;
	/// deriving the rows keeps the cells square on any shape of surface.
	/// </summary>
	protected static int GridRows(float cell, in EffectFrame frame) =>
		Math.Max(1, (int)MathF.Ceiling(frame.Height / MathF.Max(cell, 0.0001f)));

	/// <summary>Wraps a particle around both edges, with a small margin.</summary>
	protected static void Wrap(ref float x, ref float y, in EffectFrame frame, float margin = 4f)
	{
		if (x < -margin)
		{
			x = frame.Width + margin;
		}
		else if (x > frame.Width + margin)
		{
			x = -margin;
		}

		if (y < -margin)
		{
			y = frame.Height + margin;
		}
		else if (y > frame.Height + margin)
		{
			y = -margin;
		}
	}
}
