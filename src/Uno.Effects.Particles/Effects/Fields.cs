using SkiaSharp;

namespace Uno.Effects.Particles;

// The Fields family: particles carried by something they do not control. Most
// of these leave trails, because the field is invisible and the paths are the
// only way to see it.

/// <summary>
/// Particles carried by a slowly evolving noise field, leaving trails. The
/// field itself is never drawn; the accumulated paths are the picture.
/// </summary>
[Effect("Flow field", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "Particles carried by a slowly evolving noise field.",
	Tags = "generative,trails,hero",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed,Direction")]
public sealed partial class FlowField : ParticleEffect
{
	private struct Tracer
	{
		public float X;
		public float Y;
	}

	private Tracer[] _tracers = [];

	/// <summary>Only a twelfth of each frame is cleared, which is what leaves the trails.</summary>
	public override float TrailFade => 0.08f;

	protected override int NaturalCount => 140;

	protected override void Emit(in EffectFrame frame)
	{
		_tracers = new Tracer[ResolveCount(in frame)];

		for (var i = 0; i < _tracers.Length; i++)
		{
			_tracers[i] = new Tracer
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _tracers.Length; i++)
		{
			ref var tracer = ref _tracers[i];

			var angle = Noise.Sample(tracer.X * 0.012f, (tracer.Y * 0.012f) + (frame.Time * 0.08f)) * Tau * 1.5f;
			var vx = MathF.Cos(angle) * 30f;
			var vy = MathF.Sin(angle) * 30f;
			ApplyDirection(ref vx, ref vy);

			tracer.X += vx * frame.DeltaTime;
			tracer.Y += vy * frame.DeltaTime;

			// A trickle of random respawns keeps the field from settling into
			// a handful of attracting paths.
			if (tracer.X < 0 || tracer.X > frame.Width
				|| tracer.Y < 0 || tracer.Y > frame.Height
				|| Random.NextSingle() < 0.004f * TunedSteps(in frame))
			{
				tracer.X = Random.Next(frame.Width);
				tracer.Y = Random.Next(frame.Height);
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = 1.2f * ResolveSize();

		foreach (var tracer in _tracers)
		{
			Rect(canvas, tracer.X, tracer.Y, size, size, Primary, 0.7f);
		}
	}
}

/// <summary>
/// The same idea as the flow field, but following the curl of the noise rather
/// than its value, so the paths circulate instead of converging.
/// </summary>
[Effect("Curl noise", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "Paths following the curl of a noise field.",
	Tags = "generative,trails,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed,Direction")]
public sealed partial class CurlNoise : ParticleEffect
{
	private const float Epsilon = 0.5f;
	private const float Frequency = 0.02f;

	private struct Tracer
	{
		public float X;
		public float Y;
	}

	private Tracer[] _tracers = [];

	public override float TrailFade => 0.08f;

	protected override int NaturalCount => 140;

	protected override void Emit(in EffectFrame frame)
	{
		_tracers = new Tracer[ResolveCount(in frame)];

		for (var i = 0; i < _tracers.Length; i++)
		{
			_tracers[i] = new Tracer
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		var offset = frame.Time * 0.05f;

		for (var i = 0; i < _tracers.Length; i++)
		{
			ref var tracer = ref _tracers[i];

			// Curl of a scalar field is its rotated gradient, so the vertical
			// difference drives x and the horizontal difference drives y.
			var dx = (Noise.Sample(tracer.X * Frequency, ((tracer.Y + Epsilon) * Frequency) + offset)
				- Noise.Sample(tracer.X * Frequency, ((tracer.Y - Epsilon) * Frequency) + offset)) / (2f * Epsilon);
			var dy = (Noise.Sample((tracer.X + Epsilon) * Frequency, (tracer.Y * Frequency) + offset)
				- Noise.Sample((tracer.X - Epsilon) * Frequency, (tracer.Y * Frequency) + offset)) / (2f * Epsilon);

			var magnitude = MathF.Sqrt((dx * dx) + (dy * dy));
			if (magnitude <= 0)
			{
				magnitude = 1f;
			}

			var vx = dx / magnitude * 28f;
			var vy = -dy / magnitude * 28f;
			ApplyDirection(ref vx, ref vy);

			tracer.X += vx * frame.DeltaTime;
			tracer.Y += vy * frame.DeltaTime;

			if (tracer.X < 0 || tracer.X > frame.Width
				|| tracer.Y < 0 || tracer.Y > frame.Height
				|| Random.NextSingle() < 0.004f * TunedSteps(in frame))
			{
				tracer.X = Random.Next(frame.Width);
				tracer.Y = Random.Next(frame.Height);
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = 1.2f * ResolveSize();

		foreach (var tracer in _tracers)
		{
			Rect(canvas, tracer.X, tracer.Y, size, size, Accent, 0.7f);
		}
	}
}

/// <summary>Boids: alignment, cohesion and separation, drawn as heading triangles.</summary>
[Effect("Flock", EffectCategory.Fields,
	Cost = EffectCost.Heavy,
	Description = "Boids steering by alignment, cohesion and separation.",
	Tags = "emergent,organic,hero",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Flock : ParticleEffect
{
	private const float NeighbourRadiusSquared = 900f;
	private const float CrowdRadiusSquared = 100f;
	private const float MaximumSpeed = 45f;

	private struct Boid
	{
		public float X;
		public float Y;
		public float VelocityX;
		public float VelocityY;
	}

	private Boid[] _boids = [];

	protected override int NaturalCount => 36;

	protected override void Emit(in EffectFrame frame)
	{
		_boids = new Boid[ResolveCount(in frame)];

		for (var i = 0; i < _boids.Length; i++)
		{
			_boids[i] = new Boid
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				VelocityX = (Random.NextSingle() - 0.5f) * 40f,
				VelocityY = (Random.NextSingle() - 0.5f) * 40f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		// The three rules are applied in place rather than double-buffered, so
		// a boid already sees this frame's answer for the ones before it. That
		// is how the study behaved, and it settles a little faster for it.
		var scale = frame.DeltaTime * 60f;

		for (var i = 0; i < _boids.Length; i++)
		{
			ref var boid = ref _boids[i];

			float alignX = 0, alignY = 0, centreX = 0, centreY = 0, pushX = 0, pushY = 0;
			var neighbours = 0;

			for (var j = 0; j < _boids.Length; j++)
			{
				if (i == j)
				{
					continue;
				}

				var other = _boids[j];
				var dx = other.X - boid.X;
				var dy = other.Y - boid.Y;
				var distance = (dx * dx) + (dy * dy);

				if (distance >= NeighbourRadiusSquared)
				{
					continue;
				}

				neighbours++;
				alignX += other.VelocityX;
				alignY += other.VelocityY;
				centreX += other.X;
				centreY += other.Y;

				if (distance < CrowdRadiusSquared)
				{
					pushX -= dx;
					pushY -= dy;
				}
			}

			if (neighbours > 0)
			{
				boid.VelocityX += ((((alignX / neighbours) - boid.VelocityX) * 0.05f)
					+ (((centreX / neighbours) - boid.X) * 0.01f)
					+ (pushX * 0.1f)) * scale;
				boid.VelocityY += ((((alignY / neighbours) - boid.VelocityY) * 0.05f)
					+ (((centreY / neighbours) - boid.Y) * 0.01f)
					+ (pushY * 0.1f)) * scale;
			}

			// A weak pull to the middle, so the flock stays on screen.
			boid.VelocityX += ((frame.Width / 2f) - boid.X) * 0.002f * scale;
			boid.VelocityY += ((frame.Height / 2f) - boid.Y) * 0.002f * scale;

			var speed = MathF.Sqrt((boid.VelocityX * boid.VelocityX) + (boid.VelocityY * boid.VelocityY));
			if (speed > MaximumSpeed)
			{
				boid.VelocityX *= MaximumSpeed / speed;
				boid.VelocityY *= MaximumSpeed / speed;
			}

			boid.X += boid.VelocityX * frame.DeltaTime;
			boid.Y += boid.VelocityY * frame.DeltaTime;
			Wrap(ref boid.X, ref boid.Y, in frame);
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var paint = Paint;
		paint.Style = SKPaintStyle.Fill;
		paint.Color = WithAlpha(Primary, 0.9f);

		// Every boid is one triangle of a single path: one detached path a
		// frame instead of one per boid.
		var builder = ResetPathBuilder();

		foreach (var boid in _boids)
		{
			var heading = MathF.Atan2(boid.VelocityY, boid.VelocityX);

			builder.MoveTo(boid.X + (MathF.Cos(heading) * 3f * size), boid.Y + (MathF.Sin(heading) * 3f * size));
			builder.LineTo(boid.X + (MathF.Cos(heading + 2.5f) * 2f * size), boid.Y + (MathF.Sin(heading + 2.5f) * 2f * size));
			builder.LineTo(boid.X + (MathF.Cos(heading - 2.5f) * 2f * size), boid.Y + (MathF.Sin(heading - 2.5f) * 2f * size));
			builder.Close();
		}

		using var path = builder.Detach();
		canvas.DrawPath(path, paint);
	}
}

/// <summary>Bodies falling towards a central mass on decaying orbits.</summary>
[Effect("Gravity", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "Bodies falling towards a central mass on decaying orbits.",
	Tags = "physics,space,trails",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Gravity : ParticleEffect
{
	private struct Body
	{
		public float X;
		public float Y;
		public float VelocityX;
		public float VelocityY;
	}

	private Body[] _bodies = [];

	public override float TrailFade => 0.25f;

	protected override int NaturalCount => 80;

	protected override void Emit(in EffectFrame frame)
	{
		_bodies = new Body[ResolveCount(in frame)];

		for (var i = 0; i < _bodies.Length; i++)
		{
			_bodies[i] = Launch(in frame);
		}
	}

	/// <summary>
	/// Places a body on a circle around the centre and gives it a tangential
	/// speed near the circular-orbit value, so most start out orbiting rather
	/// than falling straight in.
	/// </summary>
	private Body Launch(in EffectFrame frame)
	{
		var angle = Random.Next(Tau);
		// The shorter side, so every orbit fits a wide or tall frame; floored so
		// a zero-sized frame cannot divide by zero below.
		var extent = MathF.Max(1f, MathF.Min(frame.Width, frame.Height));
		var radius = extent * (0.3f + (Random.NextSingle() * 0.18f));
		var speed = MathF.Sqrt(9000f / radius) * (0.75f + (Random.NextSingle() * 0.5f)) * Random.Sign();

		return new Body
		{
			X = (frame.Width / 2f) + (MathF.Cos(angle) * radius),
			Y = (frame.Height / 2f) + (MathF.Sin(angle) * radius),
			VelocityX = -MathF.Sin(angle) * speed,
			VelocityY = MathF.Cos(angle) * speed,
		};
	}

	protected override void Step(in EffectFrame frame)
	{
		var centreX = frame.Width / 2f;
		var centreY = frame.Height / 2f;

		for (var i = 0; i < _bodies.Length; i++)
		{
			ref var body = ref _bodies[i];

			var dx = centreX - body.X;
			var dy = centreY - body.Y;

			// The +40 softens the singularity so a near miss does not fling the
			// body off the surface.
			var distanceSquared = (dx * dx) + (dy * dy) + 40f;
			var distance = MathF.Sqrt(distanceSquared);
			var force = 9000f / distanceSquared;

			body.VelocityX += dx / distance * force * frame.DeltaTime;
			body.VelocityY += dy / distance * force * frame.DeltaTime;
			body.X += body.VelocityX * frame.DeltaTime;
			body.Y += body.VelocityY * frame.DeltaTime;

			if (distance < 8
				|| body.X < -20 || body.X > frame.Width + 20
				|| body.Y < -20 || body.Y > frame.Height + 20)
			{
				body = Launch(in frame);
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var body in _bodies)
		{
			Dot(canvas, body.X, body.Y, size, Primary, 0.9f);
		}

		Dot(canvas, frame.Width / 2f, frame.Height / 2f, 2f * size, Primary, 1f);
	}
}

/// <summary>Expanding double rings, struck at random points.</summary>
[Effect("Ripples", EffectCategory.Fields,
	Cost = EffectCost.Light,
	Description = "Expanding double rings, struck at random points.",
	Tags = "water,calm,minimal",
	CommonKnobs = "Speed,Size,Opacity,Seed")]
public sealed partial class Ripples : ParticleEffect
{
	private const int Capacity = 64;

	private struct Ring
	{
		public float X;
		public float Y;
		public float Radius;
	}

	private readonly Ring[] _rings = new Ring[Capacity];
	private int _live;
	private float _nextRing;

	protected override int NaturalCount => 1;

	protected override void Emit(in EffectFrame frame)
	{
		_live = 0;
		_nextRing = 0f;
	}

	protected override void Step(in EffectFrame frame)
	{
		var limit = MathF.Min(frame.Width, frame.Height) * 0.4f;

		_nextRing -= frame.DeltaTime;

		if (_nextRing <= 0 && _live < Capacity)
		{
			_nextRing = (Random.NextSingle() * 0.8f) + 0.4f;
			_rings[_live++] = new Ring
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Radius = 0f,
			};
		}

		for (var i = _live - 1; i >= 0; i--)
		{
			_rings[i].Radius += 25f * frame.DeltaTime;

			if (_rings[i].Radius >= limit)
			{
				_rings[i] = _rings[--_live];
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var limit = MathF.Min(frame.Width, frame.Height) * 0.4f;
		var size = ResolveSize();
		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 1f * size;

		for (var i = 0; i < _live; i++)
		{
			var ring = _rings[i];
			var alpha = MathF.Max(0f, 1f - (ring.Radius / limit));

			paint.Color = WithAlpha(Primary, alpha * 0.7f);
			canvas.DrawCircle(ring.X, ring.Y, ring.Radius, paint);

			// A second ring just behind the first, which is what makes it read
			// as water rather than as a plain expanding circle.
			if (ring.Radius > 8)
			{
				paint.Color = WithAlpha(Primary, alpha * 0.3f);
				canvas.DrawCircle(ring.X, ring.Y, ring.Radius - 8f, paint);
			}
		}
	}
}

/// <summary>Field lines traced from one pole to the other.</summary>
[Effect("Dipole", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "Field lines traced from one pole to the other.",
	Tags = "physics,science,trails",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Dipole : ParticleEffect
{
	private struct Tracer
	{
		public float X;
		public float Y;
		public float Life;
	}

	private Tracer[] _tracers = [];

	public override float TrailFade => 0.12f;

	protected override int NaturalCount => 90;

	protected override void Emit(in EffectFrame frame)
	{
		_tracers = new Tracer[ResolveCount(in frame)];

		for (var i = 0; i < _tracers.Length; i++)
		{
			_tracers[i] = new Tracer
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Life = Random.NextSingle(),
			};
		}
	}

	private void Reset(ref Tracer tracer, in EffectFrame frame)
	{
		var angle = Random.Next(Tau);
		tracer.X = (frame.Width * 0.35f) + (MathF.Cos(angle) * 5f);
		tracer.Y = (frame.Height / 2f) + (MathF.Sin(angle) * 5f);
		tracer.Life = 1f;
	}

	protected override void Step(in EffectFrame frame)
	{
		var positiveX = frame.Width * 0.35f;
		var negativeX = frame.Width * 0.65f;
		var centreY = frame.Height / 2f;

		for (var i = 0; i < _tracers.Length; i++)
		{
			ref var tracer = ref _tracers[i];

			var px = tracer.X - positiveX;
			var py = tracer.Y - centreY;
			var nx = tracer.X - negativeX;
			var ny = tracer.Y - centreY;

			var positive = (px * px) + (py * py) + 1f;
			var negative = (nx * nx) + (ny * ny) + 1f;

			// Repelled by one pole and attracted by the other; the sum of the
			// two inverse-square terms is the field line.
			var fx = (px / positive) - (nx / negative);
			var fy = (py / positive) - (ny / negative);

			var magnitude = MathF.Sqrt((fx * fx) + (fy * fy));
			if (magnitude <= 0)
			{
				magnitude = 1f;
			}

			tracer.X += fx / magnitude * 30f * frame.DeltaTime;
			tracer.Y += fy / magnitude * 30f * frame.DeltaTime;
			tracer.Life -= frame.DeltaTime * 0.3f;

			if (negative < 16f || tracer.Life < 0
				|| tracer.X < 0 || tracer.X > frame.Width
				|| tracer.Y < 0 || tracer.Y > frame.Height)
			{
				Reset(ref tracer, in frame);
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = 1.2f * ResolveSize();

		foreach (var tracer in _tracers)
		{
			Rect(canvas, tracer.X, tracer.Y, size, size, Primary, 0.8f);
		}

		Dot(canvas, frame.Width * 0.35f, frame.Height / 2f, 2.5f * ResolveSize(), Primary, 1f);
		Dot(canvas, frame.Width * 0.65f, frame.Height / 2f, 2.5f * ResolveSize(), Primary, 1f);
	}
}

/// <summary>The Lorenz attractor, plotted in x and z.</summary>
[Effect("Attractor", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "The Lorenz attractor, plotted in x and z.",
	Tags = "chaos,science,trails",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Attractor : ParticleEffect
{
	private const float Sigma = 10f;
	private const float Rho = 28f;
	private const float Beta = 8f / 3f;

	private struct Walker
	{
		public float X;
		public float Y;
		public float Z;
	}

	private Walker[] _walkers = [];

	public override float TrailFade => 0.1f;

	protected override int NaturalCount => 12;

	protected override void Emit(in EffectFrame frame)
	{
		_walkers = new Walker[ResolveCount(in frame)];

		for (var i = 0; i < _walkers.Length; i++)
		{
			_walkers[i] = new Walker
			{
				X = (Random.NextSingle() * 2f) - 1f + 0.1f,
				Y = (Random.NextSingle() * 2f) - 1f,
				Z = 20f + (Random.NextSingle() * 5f),
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		// Three sub-steps per frame at a ninth of the timestep: the system is
		// stiff enough that one Euler step per frame diverges.
		var h = frame.DeltaTime / 9f;

		for (var i = 0; i < _walkers.Length; i++)
		{
			ref var walker = ref _walkers[i];

			for (var k = 0; k < 3; k++)
			{
				var dx = Sigma * (walker.Y - walker.X);
				var dy = (walker.X * (Rho - walker.Z)) - walker.Y;
				var dz = (walker.X * walker.Y) - (Beta * walker.Z);

				walker.X += dx * h;
				walker.Y += dy * h;
				walker.Z += dz * h;
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var walker in _walkers)
		{
			Dot(canvas,
				(frame.Width / 2f) + (walker.X * frame.Width / 60f),
				(frame.Height * 0.95f) - (walker.Z * frame.Height / 55f),
				size, Primary, 0.8f);
		}
	}
}

/// <summary>A drain: particles spiral inwards, faster the closer they get.</summary>
[Effect("Vortex", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "A drain: particles spiral inwards, faster as they go.",
	Tags = "spiral,motion,trails",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Vortex : ParticleEffect
{
	private struct Speck
	{
		public float Angle;
		public float Radius;
	}

	private Speck[] _specks = [];

	public override float TrailFade => 0.2f;

	protected override int NaturalCount => 120;

	protected override void Emit(in EffectFrame frame)
	{
		var extent = MathF.Min(frame.Width, frame.Height) * 0.5f;
		_specks = new Speck[ResolveCount(in frame)];

		for (var i = 0; i < _specks.Length; i++)
		{
			_specks[i] = new Speck
			{
				Angle = Random.Next(Tau),
				Radius = Random.Next(extent),
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		var extent = MathF.Min(frame.Width, frame.Height) * 0.5f;

		for (var i = 0; i < _specks.Length; i++)
		{
			ref var speck = ref _specks[i];

			// Angular speed rises as the radius falls, which is what makes the
			// centre look like it is pulling.
			speck.Angle += 60f / (speck.Radius + 8f) * frame.DeltaTime;
			speck.Radius -= (14f + ((extent - speck.Radius) * 0.3f)) * frame.DeltaTime;

			if (speck.Radius < 2)
			{
				speck.Radius = extent;
				speck.Angle = Random.Next(Tau);
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var centreX = frame.Width / 2f;
		var centreY = frame.Height / 2f;
		var size = ResolveSize();

		foreach (var speck in _specks)
		{
			Dot(canvas,
				centreX + (MathF.Cos(speck.Angle) * speck.Radius),
				centreY + (MathF.Sin(speck.Angle) * speck.Radius),
				size, Primary, 0.9f);
		}
	}
}

/// <summary>A funnel: a cone that widens as it rises, spinning as it goes.</summary>
[Effect("Tornado", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "A cone that widens as it rises, spinning as it goes.",
	Tags = "weather,spiral,dramatic",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Tornado : ParticleEffect
{
	private struct Speck
	{
		public float Height;
		public float Angle;
		public float Rate;
	}

	private Speck[] _specks = [];

	protected override int NaturalCount => 120;

	protected override void Emit(in EffectFrame frame)
	{
		_specks = new Speck[ResolveCount(in frame)];

		for (var i = 0; i < _specks.Length; i++)
		{
			_specks[i] = new Speck
			{
				Height = Random.NextSingle(),
				Angle = Random.Next(Tau),
				Rate = Random.Next(0.5f) + 0.75f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _specks.Length; i++)
		{
			ref var speck = ref _specks[i];

			speck.Angle += 3f * speck.Rate * frame.DeltaTime;
			speck.Height += 0.15f * frame.DeltaTime;

			if (speck.Height > 1f)
			{
				speck.Height -= 1f;
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var speck in _specks)
		{
			var radius = 4f + (speck.Height * frame.Width * 0.4f);
			var y = (frame.Height * 0.92f) - (speck.Height * frame.Height * 0.85f);
			var x = (frame.Width / 2f)
				+ (MathF.Cos(speck.Angle) * radius)
				+ (MathF.Sin(frame.Time + (speck.Height * 6f)) * 4f);

			// Brightest on the near side of the spin, which gives the funnel
			// its sense of rotation.
			var facing = 0.5f + (0.5f * MathF.Sin(speck.Angle));
			Dot(canvas, x, y, (0.9f + (speck.Height * 0.6f)) * size, Primary, 0.35f + (0.5f * facing));
		}
	}
}

/// <summary>A drop of ink spreading from the centre by random walk.</summary>
[Effect("Ink", EffectCategory.Fields,
	Cost = EffectCost.Medium,
	Description = "A drop spreading from the centre by random walk.",
	Tags = "organic,trails,hero",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Ink : ParticleEffect
{
	private struct Walker
	{
		public float X;
		public float Y;
		public float Life;
	}

	private Walker[] _walkers = [];

	public override float TrailFade => 0.06f;

	protected override int NaturalCount => 160;

	protected override void Emit(in EffectFrame frame)
	{
		_walkers = new Walker[ResolveCount(in frame)];

		for (var i = 0; i < _walkers.Length; i++)
		{
			_walkers[i] = new Walker
			{
				X = frame.Width / 2f,
				Y = frame.Height / 2f,
				Life = Random.NextSingle(),
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _walkers.Length; i++)
		{
			ref var walker = ref _walkers[i];

			walker.X += (Random.NextSingle() - 0.5f) * 220f * frame.DeltaTime;
			walker.Y += (Random.NextSingle() - 0.5f) * 220f * frame.DeltaTime;
			walker.Life += frame.DeltaTime * 0.3f;

			if (walker.Life > 1f)
			{
				walker.Life = 0f;
				walker.X = frame.Width / 2f;
				walker.Y = frame.Height / 2f;
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var walker in _walkers)
		{
			// Thinning with age is what makes the edge of the stain soft.
			Dot(canvas, walker.X, walker.Y,
				((1.4f * (1f - walker.Life)) + 0.3f) * size,
				Primary, (0.5f * (1f - walker.Life)) + 0.1f);
		}
	}
}
