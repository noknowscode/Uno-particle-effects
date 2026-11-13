using SkiaSharp;

namespace Uno.Effects.Particles;

// The Paths family: motion along a defined route rather than through a field.
//
// The study sheet was drawn on square tiles, so several of these sized their
// circles off the width alone. Here they use the shorter side, which is
// identical on a square tile and stays inside the frame on a wide one.

/// <summary>
/// Stars streaming past the viewer. Each star is drawn as the streak between
/// where it was last frame and where it is now, so speed reads as length.
/// </summary>
[Effect("Starfield", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "Stars streaming past the viewer.",
	Tags = "motion,space,hero",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Starfield : ParticleEffect
{
	private struct Star
	{
		public float X;
		public float Y;
		public float Z;
		public float PreviousZ;
	}

	private Star[] _stars = [];

	protected override int NaturalCount => 120;

	protected override void Emit(in EffectFrame frame)
	{
		_stars = new Star[ResolveCount(in frame)];

		for (var i = 0; i < _stars.Length; i++)
		{
			var z = Random.NextSingle();
			_stars[i] = new Star
			{
				X = (Random.NextSingle() - 0.5f) * 2f,
				Y = (Random.NextSingle() - 0.5f) * 2f,
				Z = z,
				PreviousZ = z,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _stars.Length; i++)
		{
			ref var star = ref _stars[i];

			star.PreviousZ = star.Z;
			star.Z -= frame.DeltaTime * 0.5f;

			if (star.Z <= 0.02f)
			{
				star.Z = 1f;
				star.PreviousZ = 1f;
				star.X = (Random.NextSingle() - 0.5f) * 2f;
				star.Y = (Random.NextSingle() - 0.5f) * 2f;
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centerX = frame.Width / 2f;
		var centerY = frame.Height / 2f;
		var size = ResolveSize();

		foreach (var star in _stars)
		{
			var x1 = centerX + (star.X / star.Z * centerX * 0.5f);
			var y1 = centerY + (star.Y / star.Z * centerY * 0.5f);
			var x0 = centerX + (star.X / star.PreviousZ * centerX * 0.5f);
			var y0 = centerY + (star.Y / star.PreviousZ * centerY * 0.5f);

			var width = (((1f - star.Z) * 2f) + 0.3f) * size;
			Line(canvas, x0, y0, x1, y1, Primary, (1f - star.Z) * 0.85f, width);
		}
	}
}

/// <summary>A bright head tracing a figure, leaving a decaying tail.</summary>
[Effect("Trail", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "A bright head tracing a figure, leaving a tail.",
	Tags = "motion,trails,hero",
	CommonKnobs = "Speed,Size,Opacity,Seed")]
public sealed partial class Trail : ParticleEffect
{
	private const int Capacity = 512;

	private struct Mote
	{
		public float X;
		public float Y;
		public float Life;
		public float Radius;
	}

	private readonly Mote[] _motes = new Mote[Capacity];
	private int _live;
	private float _headX;
	private float _headY;

	protected override int NaturalCount => 2;

	public override float TrailFade => 0.12f;

	protected override void Emit(in EffectFrame frame) => _live = 0;

	protected override void Step(in EffectFrame frame)
	{
		_headX = (frame.Width / 2f) + (MathF.Sin(frame.Time * 1.3f) * frame.Width * 0.36f);
		_headY = (frame.Height / 2f) + (MathF.Sin((frame.Time * 2.1f) + 1f) * frame.Height * 0.36f);

		var spawn = SpawnThisFrame(2f, in frame);
		for (var i = 0; i < spawn && _live < Capacity; i++)
		{
			_motes[_live++] = new Mote
			{
				X = _headX + ((Random.NextSingle() - 0.5f) * 3f),
				Y = _headY + ((Random.NextSingle() - 0.5f) * 3f),
				Life = 1f,
				Radius = (Random.NextSingle() * 1.5f) + 0.5f,
			};
		}

		for (var i = _live - 1; i >= 0; i--)
		{
			ref var mote = ref _motes[i];
			mote.Life -= frame.DeltaTime * 1.5f;

			if (mote.Life <= 0)
			{
				_motes[i] = _motes[--_live];
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		for (var i = 0; i < _live; i++)
		{
			var mote = _motes[i];
			Dot(canvas, mote.X, mote.Y, mote.Radius * mote.Life * size, Primary, mote.Life * 0.6f);
		}

		Dot(canvas, _headX, _headY, 2.5f * size, Primary, 1f);
	}
}

/// <summary>Points running round a drawn circle at their own speeds.</summary>
[Effect("Loop", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "Points running round a drawn circle.",
	Tags = "geometry,loading,minimal",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Loop : ParticleEffect
{
	private struct Runner
	{
		public float Angle;
		public float Rate;
	}

	private Runner[] _runners = [];

	protected override int NaturalCount => 36;

	protected override void Emit(in EffectFrame frame)
	{
		var count = ResolveCount(in frame);
		_runners = new Runner[count];

		for (var i = 0; i < count; i++)
		{
			_runners[i] = new Runner
			{
				Angle = i / (float)count * Tau,
				Rate = Random.Next(0.3f) + 0.6f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _runners.Length; i++)
		{
			_runners[i].Angle += _runners[i].Rate * frame.DeltaTime;
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centerX = frame.Width / 2f;
		var centerY = frame.Height / 2f;
		var radius = MathF.Min(frame.Width, frame.Height) * 0.32f;
		var size = ResolveSize();

		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 1f;
		paint.Color = WithAlpha(Primary, 0.12f);
		canvas.DrawCircle(centerX, centerY, radius, paint);

		foreach (var runner in _runners)
		{
			Dot(canvas,
				centerX + (MathF.Cos(runner.Angle) * radius),
				centerY + (MathF.Sin(runner.Angle) * radius),
				1.3f * size, Primary, 0.9f);
		}
	}
}

/// <summary>Points travelling along a drawn sine wave.</summary>
[Effect("Sine path", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "Points travelling along a drawn sine wave.",
	Tags = "geometry,minimal,motion",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class SinePath : ParticleEffect
{
	private struct Traveller
	{
		public float U;
		public float Rate;
	}

	private Traveller[] _travellers = [];

	protected override int NaturalCount => 40;

	protected override void Emit(in EffectFrame frame)
	{
		_travellers = new Traveller[ResolveCount(in frame)];

		for (var i = 0; i < _travellers.Length; i++)
		{
			_travellers[i] = new Traveller
			{
				U = Random.NextSingle(),
				Rate = Random.Next(0.3f) + 0.6f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _travellers.Length; i++)
		{
			ref var traveller = ref _travellers[i];
			traveller.U = (traveller.U + (traveller.Rate * frame.DeltaTime * 0.25f)) % 1f;
		}
	}

	private static float WaveAt(float u, in EffectFrame frame) =>
		(frame.Height / 2f) + (MathF.Sin(u * Tau * 1.5f) * frame.Height * 0.22f);

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 1f;
		paint.Color = WithAlpha(Primary, 0.12f);

		var builder = ResetPathBuilder();
		builder.MoveTo(0, WaveAt(0, in frame));

		for (var x = 2f; x <= frame.Width; x += 2f)
		{
			builder.LineTo(x, WaveAt(x / frame.Width, in frame));
		}

		using (var path = builder.Detach())
		{
			canvas.DrawPath(path, paint);
		}

		foreach (var traveller in _travellers)
		{
			Dot(canvas, traveller.U * frame.Width, WaveAt(traveller.U, in frame), 1.3f * size, Primary, 0.9f);
		}
	}
}

/// <summary>Drifting nodes linked whenever they come close enough.</summary>
[Effect("Constellation", EffectCategory.Paths,
	Cost = EffectCost.Medium,
	Description = "Drifting nodes linked whenever they come close.",
	Tags = "network,geometry,hero",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Constellation : ParticleEffect
{
	private struct Node
	{
		public float X;
		public float Y;
		public float VelocityX;
		public float VelocityY;
	}

	private Node[] _nodes = [];

	protected override int NaturalCount => 30;

	protected override void Emit(in EffectFrame frame)
	{
		_nodes = new Node[ResolveCount(in frame)];

		for (var i = 0; i < _nodes.Length; i++)
		{
			_nodes[i] = new Node
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				VelocityX = (Random.NextSingle() - 0.5f) * 10f,
				VelocityY = (Random.NextSingle() - 0.5f) * 10f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _nodes.Length; i++)
		{
			ref var node = ref _nodes[i];

			node.X += node.VelocityX * frame.DeltaTime;
			node.Y += node.VelocityY * frame.DeltaTime;

			if (node.X < 0 || node.X > frame.Width)
			{
				node.VelocityX = -node.VelocityX;
			}

			if (node.Y < 0 || node.Y > frame.Height)
			{
				node.VelocityY = -node.VelocityY;
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		// Scaled by the area the nodes spread over, so a wide frame links about
		// as many neighbours as a square one. On a square this is a quarter of
		// the width, as designed.
		var reach = MathF.Sqrt(frame.Width * frame.Height) * 0.25f;
		var reachSquared = reach * reach;

		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 0.6f;

		// Every pair is tested, which is what makes this the one Paths effect
		// with a quadratic cost and a medium rating.
		for (var i = 0; i < _nodes.Length; i++)
		{
			var a = _nodes[i];
			Dot(canvas, a.X, a.Y, 1.3f * size, Primary, 0.9f);

			for (var j = i + 1; j < _nodes.Length; j++)
			{
				var b = _nodes[j];
				var distanceSquared = ((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y));

				// Square root only for the pairs that are actually linked.
				if (distanceSquared < reachSquared)
				{
					var distance = MathF.Sqrt(distanceSquared);
					paint.Style = SKPaintStyle.Stroke;
					paint.StrokeWidth = 0.6f;
					paint.Color = WithAlpha(Primary, (1f - (distance / reach)) * 0.4f);
					canvas.DrawLine(a.X, a.Y, b.X, b.Y, paint);
				}
			}
		}
	}
}

/// <summary>Nested rings with one body on each, some running backwards.</summary>
[Effect("Orbits", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "Nested rings with one body on each.",
	Tags = "geometry,space,minimal",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Orbits : ParticleEffect
{
	private struct Body
	{
		public float Radius;
		public float Angle;
		public float Rate;
	}

	private Body[] _bodies = [];

	protected override int NaturalCount => 8;

	protected override void Emit(in EffectFrame frame)
	{
		var count = ResolveCount(in frame);
		var extent = MathF.Min(frame.Width, frame.Height) * 0.45f;
		_bodies = new Body[count];

		for (var i = 0; i < count; i++)
		{
			_bodies[i] = new Body
			{
				Radius = (i + 1) / (float)(count + 1) * extent,
				Angle = Random.Next(Tau),
				Rate = (Random.Next(0.6f) + 0.3f) * Random.Sign(),
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _bodies.Length; i++)
		{
			_bodies[i].Angle += _bodies[i].Rate * frame.DeltaTime;
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centerX = frame.Width / 2f;
		var centerY = frame.Height / 2f;
		var size = ResolveSize();

		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 0.6f;
		paint.Color = WithAlpha(Primary, 0.12f);

		foreach (var body in _bodies)
		{
			canvas.DrawCircle(centerX, centerY, body.Radius, paint);
			Dot(canvas,
				centerX + (MathF.Cos(body.Angle) * body.Radius),
				centerY + (MathF.Sin(body.Angle) * body.Radius),
				1.6f * size, Primary, 0.9f);

			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = 0.6f;
			paint.Color = WithAlpha(Primary, 0.12f);
		}

		Dot(canvas, centerX, centerY, 2.5f * size, Accent, 0.9f);
	}
}

/// <summary>A grid of points riding a diagonal travelling wave.</summary>
[Effect("Wave", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "A grid of points riding a diagonal travelling wave.",
	Tags = "grid,geometry,calm",
	CommonKnobs = "Count,Speed,Size,Opacity")]
public sealed partial class Wave : ParticleEffect
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
		var size = ResolveSize();

		for (var i = 0; i < columns; i++)
		{
			for (var j = 0; j < rows; j++)
			{
				var offset = MathF.Sin((frame.Time * 2f) + (i * 0.5f) + (j * 0.3f)) * 5f;
				Dot(canvas, (i + 0.5f) * cell, ((j + 0.5f) * cell) + offset, 1.3f * size, Primary, 0.8f);
			}
		}
	}
}

/// <summary>A lissajous figure drawn by a train of points, leaving a trail.</summary>
[Effect("Lissajous", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "A lissajous figure drawn by a train of points.",
	Tags = "geometry,trails,hero",
	CommonKnobs = "Count,Speed,Size,Opacity")]
public sealed partial class Lissajous : ParticleEffect
{
	private float[] _offsets = [];

	protected override int NaturalCount => 60;

	public override float TrailFade => 0.15f;

	protected override void Emit(in EffectFrame frame)
	{
		var count = ResolveCount(in frame);
		_offsets = new float[count];

		for (var i = 0; i < count; i++)
		{
			_offsets[i] = i * 0.03f;
		}
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var offset in _offsets)
		{
			var u = frame.Time + offset;
			Dot(canvas,
				(frame.Width / 2f) + (MathF.Sin(u * 3f) * frame.Width * 0.36f),
				(frame.Height / 2f) + (MathF.Sin((u * 2f) + 1.2f) * frame.Height * 0.36f),
				1.2f * size, Primary, 0.9f);
		}
	}
}

/// <summary>A two-armed spiral turning faster towards the centre.</summary>
[Effect("Galaxy", EffectCategory.Paths,
	Cost = EffectCost.Medium,
	Description = "A two-armed spiral turning faster towards the centre.",
	Tags = "space,hero,dense",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Galaxy : ParticleEffect
{
	private struct Star
	{
		public float Radius;
		public float Angle;
		public float Rate;
		public float Depth;
	}

	private Star[] _stars = [];

	protected override int NaturalCount => 220;

	protected override void Emit(in EffectFrame frame)
	{
		var extent = MathF.Min(frame.Width, frame.Height) * 0.45f;
		_stars = new Star[ResolveCount(in frame)];

		for (var i = 0; i < _stars.Length; i++)
		{
			// The 0.7 power concentrates stars towards the middle; the two arms
			// come from snapping the base angle to 0 or pi.
			var radius = MathF.Pow(Random.NextSingle(), 0.7f) * extent;

			_stars[i] = new Star
			{
				Radius = radius,
				Angle = (MathF.Floor(Random.NextSingle() * 2f) * MathF.PI)
					+ (radius * 0.06f)
					+ ((Random.NextSingle() - 0.5f) * (0.6f + (radius * 0.004f))),
				Rate = 1.2f / (0.3f + (radius * 0.03f)),
				Depth = Random.NextSingle(),
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _stars.Length; i++)
		{
			_stars[i].Angle += _stars[i].Rate * frame.DeltaTime;
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centerX = frame.Width / 2f;
		var centerY = frame.Height / 2f;
		var extent = MathF.Min(frame.Width, frame.Height) * 0.45f;
		var size = ResolveSize();

		foreach (var star in _stars)
		{
			Dot(canvas,
				centerX + (MathF.Cos(star.Angle) * star.Radius),
				centerY + (MathF.Sin(star.Angle) * star.Radius * 0.55f),
				(0.4f + (star.Depth * 0.9f)) * size,
				Primary,
				0.35f + (0.6f * (1f - (star.Radius / extent))));
		}
	}
}

/// <summary>A twisted ladder, the two rails swapping depth as they turn.</summary>
[Effect("Helix", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "A twisted ladder, its rails swapping depth as they turn.",
	Tags = "geometry,science,hero",
	CommonKnobs = "Speed,Size,Opacity")]
public sealed partial class Helix : ParticleEffect
{
	private const int Rungs = 22;

	protected override int NaturalCount => Rungs;

	protected override void Emit(in EffectFrame frame)
	{
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var paint = Paint;

		for (var i = 0; i < Rungs; i++)
		{
			var y = (i + 0.5f) / Rungs * frame.Height;
			var angle = (frame.Time * 1.5f) + (i * 0.45f);
			var spread = MathF.Cos(angle) * frame.Width * 0.25f;
			var x1 = (frame.Width / 2f) + spread;
			var x2 = (frame.Width / 2f) - spread;
			var depth = MathF.Sin(angle);

			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = 1f;
			paint.Color = WithAlpha(Primary, 0.15f);
			canvas.DrawLine(x1, y, x2, y, paint);

			// The two rails read as near and far because size and alpha move in
			// opposite directions as the ladder turns.
			Dot(canvas, x1, y, (1f + ((depth + 1f) * 0.8f)) * size, Primary, 0.4f + ((depth + 1f) * 0.3f));
			Dot(canvas, x2, y, (1f + ((1f - depth) * 0.8f)) * size, Accent, 0.4f + ((1f - depth) * 0.3f));
		}
	}
}

/// <summary>Blocks falling down columns, snapped to the grid.</summary>
[Effect("Cascade", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "Blocks falling down columns, snapped to the grid.",
	Tags = "grid,digital,motion",
	CommonKnobs = "Speed,Size,Opacity,Seed,Gravity")]
public sealed partial class Cascade : ParticleEffect
{
	private const int Columns = 16;

	private struct Column
	{
		public float Y;
		public float Speed;
	}

	private Column[] _columns = [];

	protected override int NaturalCount => Columns;

	public override float TrailFade => 0.12f;

	protected override void Emit(in EffectFrame frame)
	{
		_columns = new Column[Columns];

		for (var i = 0; i < Columns; i++)
		{
			_columns[i] = new Column
			{
				Y = -Random.Next(frame.Height),
				Speed = (Random.NextSingle() * 60f) + 50f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		var gravity = (float)Gravity;

		for (var i = 0; i < _columns.Length; i++)
		{
			ref var column = ref _columns[i];
			column.Y += column.Speed * gravity * frame.DeltaTime;

			if (column.Y > frame.Height + 10)
			{
				column.Y = -Random.Next(frame.Height);
				column.Speed = (Random.NextSingle() * 60f) + 50f;
			}
			else if (column.Y < -frame.Height - 10)
			{
				// Reversed gravity: re-enter from below.
				column.Y = frame.Height + Random.Next(frame.Height);
				column.Speed = (Random.NextSingle() * 60f) + 50f;
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var cell = frame.Width / Columns;
		var block = cell * 0.4f * ResolveSize();
		var inset = (cell - block) / 2f;

		for (var i = 0; i < Columns; i++)
		{
			// Snapping y to the cell height is what makes it step rather than
			// slide, and is the whole character of the effect.
			var y = MathF.Floor(_columns[i].Y / cell) * cell;
			Rect(canvas, (i * cell) + inset, y + inset, block, block, Primary, 0.9f);
		}
	}
}

/// <summary>A ring that swells and contracts, its edge roughened by noise.</summary>
[Effect("Breath", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "A ring that swells and contracts, roughened by noise.",
	Tags = "calm,organic,minimal",
	CommonKnobs = "Count,Speed,Size,Opacity")]
public sealed partial class Breath : ParticleEffect
{
	private int _count;

	protected override int NaturalCount => 64;

	protected override void Emit(in EffectFrame frame) => _count = ResolveCount(in frame);

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centerX = frame.Width / 2f;
		var centerY = frame.Height / 2f;
		var extent = MathF.Min(frame.Width, frame.Height);
		var breath = extent * (0.25f + (0.08f * MathF.Sin(frame.Time * 1.2f)));
		var size = ResolveSize();

		for (var i = 0; i < _count; i++)
		{
			var angle = i / (float)_count * Tau;

			// Sampling the noise around a circle keeps the roughness attached to
			// the ring rather than to the screen.
			var wobble = (Noise.Sample(
				(MathF.Cos(angle) * 2f) + 5f,
				(MathF.Sin(angle) * 2f) + (frame.Time * 0.4f)) - 0.5f) * 20f;

			var radius = breath + wobble;
			Dot(canvas, centerX + (MathF.Cos(angle) * radius), centerY + (MathF.Sin(angle) * radius),
				1.3f * size, Primary, 0.85f);
		}
	}
}

/// <summary>The sunflower spiral, with a pulse running out from the centre.</summary>
[Effect("Phyllotaxis", EffectCategory.Paths,
	Cost = EffectCost.Medium,
	Description = "The sunflower spiral, with a pulse running outwards.",
	Tags = "geometry,organic,hero",
	CommonKnobs = "Count,Speed,Size,Opacity")]
public sealed partial class Phyllotaxis : ParticleEffect
{
	/// <summary>The golden angle, in radians.</summary>
	private const float GoldenAngle = 2.39996f;

	private int _count;

	protected override int NaturalCount => 220;

	protected override void Emit(in EffectFrame frame) => _count = ResolveCount(in frame);

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centerX = frame.Width / 2f;
		var centerY = frame.Height / 2f;
		var extent = MathF.Min(frame.Width, frame.Height) * 0.45f;
		var size = ResolveSize();

		for (var i = 0; i < _count; i++)
		{
			var radius = MathF.Sqrt(i / (float)_count) * extent;
			var angle = (i * GoldenAngle) + (frame.Time * 0.2f);
			var pulse = 0.5f + (0.5f * MathF.Sin((frame.Time * 2f) - (radius * 0.1f)));

			Dot(canvas,
				centerX + (MathF.Cos(angle) * radius),
				centerY + (MathF.Sin(angle) * radius),
				(0.6f + (1.2f * pulse)) * size, Primary, 0.85f);
		}
	}
}

/// <summary>A 5x5x5 lattice of points turning in perspective.</summary>
[Effect("Cube", EffectCategory.Paths,
	Cost = EffectCost.Light,
	Description = "A lattice of points turning in perspective.",
	Tags = "3d,geometry,minimal",
	CommonKnobs = "Speed,Size,Opacity")]
public sealed partial class Cube : ParticleEffect
{
	private const int Side = 5;

	private SKPoint3[] _points = [];

	protected override int NaturalCount => Side * Side * Side;

	protected override void Emit(in EffectFrame frame)
	{
		_points = new SKPoint3[Side * Side * Side];
		var index = 0;

		for (var i = 0; i < Side; i++)
		{
			for (var j = 0; j < Side; j++)
			{
				for (var k = 0; k < Side; k++)
				{
					_points[index++] = new SKPoint3(
						(i / (float)(Side - 1)) - 0.5f,
						(j / (float)(Side - 1)) - 0.5f,
						(k / (float)(Side - 1)) - 0.5f);
				}
			}
		}
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame) =>
		Project(canvas, _points, in frame);
}

/// <summary>Points spread evenly over a sphere, turning in perspective.</summary>
[Effect("Sphere", EffectCategory.Paths,
	Cost = EffectCost.Medium,
	Description = "Points spread evenly over a sphere, turning.",
	Tags = "3d,geometry,hero",
	CommonKnobs = "Count,Speed,Size,Opacity")]
public sealed partial class Sphere : ParticleEffect
{
	private const float GoldenAngle = 2.39996f;

	private SKPoint3[] _points = [];

	protected override int NaturalCount => 160;

	protected override void Emit(in EffectFrame frame)
	{
		var count = ResolveCount(in frame);
		_points = new SKPoint3[count];

		// Fibonacci sphere: even spacing without any clustering at the poles.
		for (var i = 0; i < count; i++)
		{
			var y = 1f - (2f * (i + 0.5f) / count);
			var ring = MathF.Sqrt(MathF.Max(0f, 1f - (y * y)));
			var angle = i * GoldenAngle;

			_points[i] = new SKPoint3(
				MathF.Cos(angle) * ring * 0.5f,
				y * 0.5f,
				MathF.Sin(angle) * ring * 0.5f);
		}
	}

	protected override void Step(in EffectFrame frame)
	{
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame) =>
		Project(canvas, _points, in frame);
}
