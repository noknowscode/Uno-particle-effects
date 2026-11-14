using Microsoft.UI.Xaml;
using SkiaSharp;

namespace Uno.Effects.Particles;

// The Light family: things that emit rather than reflect. Several of these are
// emitters with no fixed population, so they keep a fixed-capacity pool and
// remove dead particles by swapping the last live one into the gap. That keeps
// the per-frame allocation at zero, which the budget in the recommendation asks
// for and a List<T> of structs would quietly break.

/// <summary>
/// Wandering lights that blink on a cubed sine, so most of the time most of
/// them are dark. Drawn in the accent colour.
/// </summary>
[Effect("Fireflies", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "Wandering lights that blink in and out.",
	Tags = "ambient,glow,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Fireflies : ParticleEffect
{
	private struct Fly
	{
		public float X;
		public float Y;
		public float Heading;
		public float Phase;
		public float Frequency;
	}

	private Fly[] _flies = [];

	/// <summary>How quickly each light blinks.</summary>
	[EffectProperty(0.1, 4, Step = 0.05, Group = "Fireflies")]
	public double BlinkRate
	{
		get => (double)GetValue(BlinkRateProperty);
		set => SetValue(BlinkRateProperty, value);
	}

	public static readonly DependencyProperty BlinkRateProperty = DependencyProperty.Register(
		nameof(BlinkRate), typeof(double), typeof(Fireflies), new PropertyMetadata(1d, OnVisualChanged));

	protected override int NaturalCount => 22;

	protected override void Emit(in EffectFrame frame)
	{
		_flies = new Fly[ResolveCount(in frame)];

		for (var i = 0; i < _flies.Length; i++)
		{
			_flies[i] = new Fly
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Heading = Random.Next(Tau),
				Phase = Random.Next(Tau),
				Frequency = Random.Next(0.6f) + 0.5f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		for (var i = 0; i < _flies.Length; i++)
		{
			ref var fly = ref _flies[i];

			fly.Heading += (Random.NextSingle() - 0.5f) * 12f * frame.DeltaTime;
			fly.X += MathF.Cos(fly.Heading) * 14f * frame.DeltaTime;
			fly.Y += MathF.Sin(fly.Heading) * 14f * frame.DeltaTime;
			Wrap(ref fly.X, ref fly.Y, in frame);
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var rate = (float)BlinkRate;

		foreach (var fly in _flies)
		{
			var blink = MathF.Max(0f, MathF.Sin((frame.Time * fly.Frequency * 2f * rate) + fly.Phase));
			var brightness = blink * blink * blink;

			if (brightness > 0.02f)
			{
				Glow(canvas, fly.X, fly.Y, 1.4f * size, Accent, brightness);
			}
		}
	}
}

/// <summary>Sparks thrown up from a point and falling back under gravity.</summary>
[Effect("Sparks", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "Sparks thrown up from a point and falling back.",
	Tags = "emitter,fire,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed,Gravity")]
public sealed partial class Sparks : ParticleEffect
{
	private const int Capacity = 512;

	private struct Spark
	{
		public float X;
		public float Y;
		public float PreviousX;
		public float PreviousY;
		public float VelocityX;
		public float VelocityY;
		public float Life;
	}

	private readonly Spark[] _sparks = new Spark[Capacity];
	private int _live;

	/// <summary>Sparks emitted per frame, before the device tier scales it.</summary>
	protected override int NaturalCount => 3;

	public override float TrailFade => 0.35f;

	protected override void Emit(in EffectFrame frame) => _live = 0;

	protected override void Step(in EffectFrame frame)
	{
		var gravity = (float)Gravity;
		var spawn = SpawnThisFrame(ResolveCount(in frame), in frame);

		for (var i = 0; i < spawn && _live < Capacity; i++)
		{
			_sparks[_live++] = new Spark
			{
				X = frame.Width / 2f,
				Y = frame.Height * 0.9f,
				PreviousX = frame.Width / 2f,
				PreviousY = frame.Height * 0.9f,
				VelocityX = (Random.NextSingle() - 0.5f) * 120f,
				VelocityY = (-Random.NextSingle() * 160f) - 40f,
				Life = 1f,
			};
		}

		for (var i = _live - 1; i >= 0; i--)
		{
			ref var spark = ref _sparks[i];

			spark.PreviousX = spark.X;
			spark.PreviousY = spark.Y;
			spark.VelocityY += 160f * gravity * frame.DeltaTime;
			spark.X += spark.VelocityX * frame.DeltaTime;
			spark.Y += spark.VelocityY * frame.DeltaTime;
			spark.Life -= frame.DeltaTime * 1.2f;

			if (spark.Life <= 0 || spark.Y >= frame.Height)
			{
				_sparks[i] = _sparks[--_live];
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		for (var i = 0; i < _live; i++)
		{
			var spark = _sparks[i];
			Line(canvas, spark.PreviousX, spark.PreviousY, spark.X, spark.Y,
				Accent, MathF.Max(spark.Life, 0f), 1.2f * size);
		}
	}
}

/// <summary>A still field of points, each pulsing on its own clock.</summary>
[Effect("Twinkle", EffectCategory.Light,
	Cost = EffectCost.Light,
	Description = "A still field of points, each pulsing on its own clock.",
	Tags = "ambient,stars,background",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Twinkle : ParticleEffect
{
	private struct Point
	{
		public float X;
		public float Y;
		public float Radius;
		public float Phase;
		public float Frequency;
	}

	private Point[] _points = [];

	protected override int NaturalCount => 110;

	protected override void Emit(in EffectFrame frame)
	{
		_points = new Point[ResolveCount(in frame)];

		for (var i = 0; i < _points.Length; i++)
		{
			_points[i] = new Point
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Radius = Random.Next(1.1f) + 0.3f,
				Phase = Random.Next(Tau),
				Frequency = Random.Next(2f) + 0.5f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		// Nothing moves; the whole effect is in the alpha.
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var point in _points)
		{
			var pulse = 0.5f + (0.5f * MathF.Sin((frame.Time * point.Frequency) + point.Phase));
			Dot(canvas, point.X, point.Y, point.Radius * size, Primary, 0.15f + (0.85f * pulse * pulse * pulse));
		}
	}
}

/// <summary>Large soft lights drifting on slow lissajous paths.</summary>
[Effect("Orbs", EffectCategory.Light,
	Cost = EffectCost.Light,
	Description = "Large soft lights drifting on slow paths.",
	Tags = "ambient,glow,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Orbs : ParticleEffect
{
	private struct Orb
	{
		public float X;
		public float Y;
		public float Radius;
		public float Phase;
		public float Rate;
	}

	private Orb[] _orbs = [];

	protected override int NaturalCount => 7;

	protected override void Emit(in EffectFrame frame)
	{
		_orbs = new Orb[ResolveCount(in frame)];

		for (var i = 0; i < _orbs.Length; i++)
		{
			_orbs[i] = new Orb
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Radius = Random.Next(5f) + 4f,
				Phase = Random.Next(Tau),
				Rate = Random.Next(0.3f) + 0.2f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		// Position is a pure function of time; nothing to integrate.
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var orb in _orbs)
		{
			var x = orb.X + (MathF.Sin((frame.Time * orb.Rate) + orb.Phase) * 18f);
			var y = orb.Y + (MathF.Cos((frame.Time * orb.Rate * 0.7f) + orb.Phase) * 14f);
			var radius = orb.Radius * size;

			for (var i = 5; i > 0; i--)
			{
				Dot(canvas, x, y, radius * i * 0.6f, Accent, 0.05f);
			}

			Dot(canvas, x, y, radius * 0.5f, Accent, 0.7f);
		}
	}
}

/// <summary>Embers rising and fading, brightest at the bottom of the frame.</summary>
[Effect("Embers", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "Embers rising and fading as they climb.",
	Tags = "fire,glow,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed,Gravity")]
public sealed partial class Embers : ParticleEffect
{
	private struct Ember
	{
		public float X;
		public float Y;
		public float Radius;
		public float Phase;
		public float Speed;
	}

	private Ember[] _embers = [];

	protected override int NaturalCount => 50;

	protected override void Emit(in EffectFrame frame)
	{
		_embers = new Ember[ResolveCount(in frame)];

		for (var i = 0; i < _embers.Length; i++)
		{
			_embers[i] = new Ember
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Radius = Random.Next(1.3f) + 0.5f,
				Phase = Random.Next(Tau),
				Speed = Random.Next(0.6f) + 0.4f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		var gravity = (float)Gravity;

		for (var i = 0; i < _embers.Length; i++)
		{
			ref var ember = ref _embers[i];

			ember.Y -= (14f + (ember.Radius * 10f)) * ember.Speed * gravity * frame.DeltaTime;
			ember.X += (MathF.Sin((frame.Time * 1.5f) + ember.Phase) * 10f * frame.DeltaTime)
				+ ((Noise.Sample(ember.Y * 0.02f, ember.Phase) - 0.5f) * 12f * frame.DeltaTime);

			if (ember.Y < -3)
			{
				ember.Y = frame.Height + 3;
				ember.X = Random.Next(frame.Width);
			}
			else if (ember.Y > frame.Height + 3)
			{
				// Negative gravity sinks embers; recycle them at the top.
				ember.Y = -3;
				ember.X = Random.Next(frame.Width);
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var ember in _embers)
		{
			var flicker = 0.5f + (0.5f * MathF.Sin((frame.Time * 6f) + ember.Phase));

			// Dimming with height is what makes the column read as heat rising
			// rather than as particles moving.
			var height = MathF.Max(0f, ember.Y / frame.Height);
			Glow(canvas, ember.X, ember.Y, ember.Radius * size, Accent, (0.4f + (0.6f * flicker)) * height);
		}
	}
}

/// <summary>A jagged bolt struck at random intervals, fading fast.</summary>
[Effect("Lightning", EffectCategory.Light,
	Cost = EffectCost.Light,
	Description = "A jagged bolt struck at random intervals.",
	Tags = "weather,flash,dramatic",
	CommonKnobs = "Speed,Size,Opacity,Seed")]
public sealed partial class Lightning : ParticleEffect
{
	private const int MaxSegments = 96;

	private readonly SKPoint[] _bolt = new SKPoint[MaxSegments];
	private int _segments;
	private float _nextStrike;
	private float _flash;

	protected override int NaturalCount => 1;

	public override float TrailFade => 0.35f;

	protected override void Emit(in EffectFrame frame)
	{
		_segments = 0;
		_flash = 0f;
		_nextStrike = 0f;
	}

	protected override void Step(in EffectFrame frame)
	{
		_nextStrike -= frame.DeltaTime;

		if (_nextStrike <= 0)
		{
			_nextStrike = Random.Next(1.5f) + 0.4f;
			_flash = 1f;
			Strike(in frame);
		}

		if (_flash > 0)
		{
			_flash -= frame.DeltaTime * 5f;
		}
	}

	private void Strike(in EffectFrame frame)
	{
		var x = frame.Width * (0.3f + (Random.NextSingle() * 0.4f));
		var y = 0f;

		_segments = 0;
		_bolt[_segments++] = new SKPoint(x, y);

		while (y < frame.Height && _segments < MaxSegments)
		{
			y += (Random.NextSingle() * 10f) + 4f;
			x += (Random.NextSingle() - 0.5f) * 14f;
			_bolt[_segments++] = new SKPoint(x, y);
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		if (_flash <= 0 || _segments < 2)
		{
			return;
		}

		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 1.2f * ResolveSize();
		paint.Color = WithAlpha(Primary, _flash);

		for (var i = 1; i < _segments; i++)
		{
			canvas.DrawLine(_bolt[i - 1], _bolt[i], paint);
		}
	}
}

/// <summary>Out-of-focus highlights: filled discs with a brighter rim.</summary>
[Effect("Bokeh", EffectCategory.Light,
	Cost = EffectCost.Light,
	Description = "Out-of-focus highlights, filled with a brighter rim.",
	Tags = "ambient,soft,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Bokeh : ParticleEffect
{
	private struct Highlight
	{
		public float X;
		public float Y;
		public float Radius;
		public float Phase;
		public float Rate;
	}

	private Highlight[] _highlights = [];

	protected override int NaturalCount => 16;

	protected override void Emit(in EffectFrame frame)
	{
		_highlights = new Highlight[ResolveCount(in frame)];

		for (var i = 0; i < _highlights.Length; i++)
		{
			_highlights[i] = new Highlight
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Radius = Random.Next(9f) + 4f,
				Phase = Random.Next(Tau),
				Rate = Random.Next(0.3f) + 0.1f,
			};
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		// Drift is derived from time in Draw.
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var paint = Paint;

		foreach (var highlight in _highlights)
		{
			var x = highlight.X + (MathF.Sin((frame.Time * highlight.Rate) + highlight.Phase) * 10f);
			var y = highlight.Y + (MathF.Cos((frame.Time * highlight.Rate * 0.8f) + highlight.Phase) * 10f);
			var radius = highlight.Radius * size;
			var alpha = 0.08f + (0.1f * (0.5f + (0.5f * MathF.Sin((frame.Time * 0.7f) + highlight.Phase))));

			Dot(canvas, x, y, radius, Accent, alpha);

			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = 1f;
			paint.Color = WithAlpha(Accent, alpha * 1.5f);
			canvas.DrawCircle(x, y, radius, paint);
		}
	}
}

/// <summary>Shells that burst into a fading shower, one every second or two.</summary>
[Effect("Fireworks", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "Shells that burst into a fading shower.",
	Tags = "celebration,emitter,dramatic",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed,Gravity")]
public sealed partial class Fireworks : ParticleEffect
{
	private const int Capacity = 640;

	private struct Mote
	{
		public float X;
		public float Y;
		public float VelocityX;
		public float VelocityY;
		public float Life;
		public int Colour;
	}

	private readonly Mote[] _motes = new Mote[Capacity];
	private int _live;
	private float _nextShell;

	/// <summary>Motes per shell, before the device tier scales it.</summary>
	protected override int NaturalCount => 50;

	public override float TrailFade => 0.2f;

	protected override void Emit(in EffectFrame frame)
	{
		_live = 0;
		_nextShell = 0.5f;
	}

	protected override void Step(in EffectFrame frame)
	{
		var gravity = (float)Gravity;

		_nextShell -= frame.DeltaTime;

		if (_nextShell <= 0)
		{
			_nextShell = (Random.NextSingle() * 1.2f) + 0.8f;

			var x = frame.Width * (0.25f + (Random.NextSingle() * 0.5f));
			var y = frame.Height * (0.2f + (Random.NextSingle() * 0.4f));
			var colour = Random.NextSingle() < 0.5f ? 0 : 1;
			var count = ResolveCount(in frame);

			for (var i = 0; i < count && _live < Capacity; i++)
			{
				var angle = Random.Next(Tau);
				var speed = (Random.NextSingle() * 60f) + 20f;

				_motes[_live++] = new Mote
				{
					X = x,
					Y = y,
					VelocityX = MathF.Cos(angle) * speed,
					VelocityY = MathF.Sin(angle) * speed,
					Life = 1f,
					Colour = colour,
				};
			}
		}

		var damping = PerTunedStep(0.985f, in frame);

		for (var i = _live - 1; i >= 0; i--)
		{
			ref var mote = ref _motes[i];

			mote.VelocityY += 40f * gravity * frame.DeltaTime;
			mote.VelocityX *= damping;
			mote.VelocityY *= damping;
			mote.X += mote.VelocityX * frame.DeltaTime;
			mote.Y += mote.VelocityY * frame.DeltaTime;
			mote.Life -= frame.DeltaTime * 0.7f;

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
			Dot(canvas, mote.X, mote.Y, size, PaletteAt(mote.Colour), MathF.Max(0f, mote.Life));
		}
	}
}

/// <summary>A jet thrown up from a line and falling back onto it.</summary>
[Effect("Fountain", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "A jet thrown up from a line and falling back onto it.",
	Tags = "emitter,water,rising",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed,Gravity")]
public sealed partial class Fountain : ParticleEffect
{
	private const int Capacity = 640;

	private struct Drop
	{
		public float X;
		public float Y;
		public float VelocityX;
		public float VelocityY;
	}

	private readonly Drop[] _drops = new Drop[Capacity];
	private int _live;

	/// <summary>Drops emitted per frame, before the device tier scales it.</summary>
	protected override int NaturalCount => 3;

	protected override void Emit(in EffectFrame frame) => _live = 0;

	protected override void Step(in EffectFrame frame)
	{
		var gravity = (float)Gravity;
		var spawn = SpawnThisFrame(ResolveCount(in frame), in frame);

		for (var i = 0; i < spawn && _live < Capacity; i++)
		{
			_drops[_live++] = new Drop
			{
				X = frame.Width / 2f,
				Y = frame.Height * 0.85f,
				VelocityX = (Random.NextSingle() - 0.5f) * 40f,
				VelocityY = (-Random.NextSingle() * 40f) - 110f,
			};
		}

		var surface = frame.Height * 0.88f;

		for (var i = _live - 1; i >= 0; i--)
		{
			ref var drop = ref _drops[i];

			drop.VelocityY += 140f * gravity * frame.DeltaTime;
			drop.X += drop.VelocityX * frame.DeltaTime;
			drop.Y += drop.VelocityY * frame.DeltaTime;

			// A drop that never comes down (zero or negative gravity) leaves at
			// the top, or the pool fills and the jet stops.
			if (drop.Y >= surface || drop.Y < -10f)
			{
				_drops[i] = _drops[--_live];
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		for (var i = 0; i < _live; i++)
		{
			Dot(canvas, _drops[i].X, _drops[i].Y, size, Primary, 0.8f);
		}

		// The basin the jet falls onto, so the drops end somewhere rather than
		// simply vanishing.
		Rect(canvas, frame.Width * 0.2f, frame.Height * 0.88f, frame.Width * 0.6f, 1f, Primary, 0.25f);
	}
}

/// <summary>A ring thrown outwards from the centre every couple of seconds.</summary>
[Effect("Burst", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "A ring thrown outwards from the centre.",
	Tags = "emitter,impact,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Burst : ParticleEffect
{
	private struct Shard
	{
		public float X;
		public float Y;
		public float VelocityX;
		public float VelocityY;
		public float Life;
	}

	private Shard[] _shards = [];
	private float _nextBurst;

	protected override int NaturalCount => 90;

	public override float TrailFade => 0.25f;

	protected override void Emit(in EffectFrame frame)
	{
		_shards = new Shard[ResolveCount(in frame)];
		_nextBurst = 0f;
	}

	protected override void Step(in EffectFrame frame)
	{
		_nextBurst -= frame.DeltaTime;

		if (_nextBurst <= 0)
		{
			_nextBurst = 2f;

			for (var i = 0; i < _shards.Length; i++)
			{
				var angle = Random.Next(Tau);
				var speed = (Random.NextSingle() * 50f) + 30f;

				_shards[i] = new Shard
				{
					X = frame.Width / 2f,
					Y = frame.Height / 2f,
					VelocityX = MathF.Cos(angle) * speed,
					VelocityY = MathF.Sin(angle) * speed,
					Life = 1f,
				};
			}
		}

		var damping = PerTunedStep(0.97f, in frame);

		for (var i = 0; i < _shards.Length; i++)
		{
			ref var shard = ref _shards[i];

			shard.VelocityX *= damping;
			shard.VelocityY *= damping;
			shard.X += shard.VelocityX * frame.DeltaTime;
			shard.Y += shard.VelocityY * frame.DeltaTime;
			shard.Life -= frame.DeltaTime * 0.5f;
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var shard in _shards)
		{
			if (shard.Life > 0)
			{
				Dot(canvas, shard.X, shard.Y, 1.2f * size, Accent, shard.Life);
			}
		}
	}
}

/// <summary>Four-pointed stars that flare up and fade out where they appear.</summary>
[Effect("Sparkle", EffectCategory.Light,
	Cost = EffectCost.Light,
	Description = "Four-pointed stars that flare up and fade out.",
	Tags = "accent,detail,celebration",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Sparkle : ParticleEffect
{
	private const int Capacity = 192;

	private struct Star
	{
		public float X;
		public float Y;
		public float Life;
		public float Radius;
	}

	private readonly Star[] _stars = new Star[Capacity];
	private int _live;

	/// <summary>Scales how often a new star appears, not a population.</summary>
	protected override int NaturalCount => 1;

	protected override void Emit(in EffectFrame frame) => _live = 0;

	protected override void Step(in EffectFrame frame)
	{
		// Count scales the spawn rate: whole stars per step, plus a chance at
		// one more for the fraction. At the default this is the original 0.3
		// chance per step, drawing the same random numbers.
		var rate = 0.3f * frame.Density * (Count > 0 ? Count : NaturalCount) * TunedSteps(in frame);
		var spawn = (int)rate;
		if (Random.NextSingle() < rate - spawn)
		{
			spawn++;
		}

		for (var k = 0; k < spawn && _live < Capacity; k++)
		{
			_stars[_live++] = new Star
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Life = 0f,
				Radius = (Random.NextSingle() * 3f) + 2f,
			};
		}

		for (var i = _live - 1; i >= 0; i--)
		{
			ref var star = ref _stars[i];
			star.Life += frame.DeltaTime * 2f;

			if (star.Life >= 1f)
			{
				_stars[i] = _stars[--_live];
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var paint = Paint;

		for (var i = 0; i < _live; i++)
		{
			var star = _stars[i];

			// A half-sine over the star's life: in and out with no hard edge.
			var brightness = MathF.Sin(MathF.Min(star.Life, 1f) * MathF.PI);
			var radius = star.Radius * brightness * size;

			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = 0.8f;
			paint.Color = WithAlpha(Primary, brightness);
			canvas.DrawLine(star.X - radius, star.Y, star.X + radius, star.Y, paint);
			canvas.DrawLine(star.X, star.Y - radius, star.X, star.Y + radius, paint);

			Dot(canvas, star.X, star.Y, radius * 0.3f, Primary, brightness);
		}
	}
}

/// <summary>Coloured gas clouds over a still starfield.</summary>
[Effect("Nebula", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "Coloured gas clouds over a still starfield.",
	Tags = "space,colour,soft",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed")]
public sealed partial class Nebula : ParticleEffect
{
	private struct Cloud
	{
		public float X;
		public float Y;
		public float Radius;
		public float Phase;
		public float Rate;
		public int Colour;
	}

	private Cloud[] _clouds = [];
	private SKPoint[] _stars = [];

	protected override int NaturalCount => 12;

	protected override void Emit(in EffectFrame frame)
	{
		_clouds = new Cloud[ResolveCount(in frame)];

		for (var i = 0; i < _clouds.Length; i++)
		{
			_clouds[i] = new Cloud
			{
				X = Random.Next(frame.Width),
				Y = Random.Next(frame.Height),
				Radius = Random.Next(18f) + 12f,
				Phase = Random.Next(Tau),
				Rate = Random.Next(0.2f) + 0.1f,

				// Starts on the accent rather than the ink, so the clouds read
				// as colour and the stars as white.
				Colour = (i % 4) + 1,
			};
		}

		_stars = new SKPoint[40];

		for (var i = 0; i < _stars.Length; i++)
		{
			_stars[i] = new SKPoint(Random.Next(frame.Width), Random.Next(frame.Height));
		}
	}

	protected override void Step(in EffectFrame frame)
	{
		// Cloud drift is derived from time in Draw; the stars do not move.
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		foreach (var cloud in _clouds)
		{
			var x = cloud.X + (MathF.Sin((frame.Time * cloud.Rate) + cloud.Phase) * 14f);
			var y = cloud.Y + (MathF.Cos((frame.Time * cloud.Rate * 1.3f) + cloud.Phase) * 14f);
			var radius = cloud.Radius * size;

			for (var i = 4; i > 0; i--)
			{
				Dot(canvas, x, y, radius * i / 4f, PaletteAt(cloud.Colour), 0.05f);
			}
		}

		foreach (var star in _stars)
		{
			Dot(canvas, star.X, star.Y, 0.6f * size, Primary, 0.5f);
		}
	}
}

/// <summary>Streaks crossing the frame with a gradient tail.</summary>
[Effect("Meteors", EffectCategory.Light,
	Cost = EffectCost.Light,
	Description = "Streaks crossing the frame with a gradient tail.",
	Tags = "space,motion,sparse",
	CommonKnobs = "Speed,Size,Opacity,Seed")]
public sealed partial class Meteors : ParticleEffect
{
	private const int Capacity = 64;

	private struct Meteor
	{
		public float X;
		public float Y;
		public float Speed;
		public float Length;
	}

	private readonly Meteor[] _meteors = new Meteor[Capacity];
	private readonly SKColor[] _tailColors = new SKColor[2];
	private int _live;
	private float _nextMeteor;

	protected override int NaturalCount => 1;

	protected override void Emit(in EffectFrame frame)
	{
		_live = 0;
		_nextMeteor = 0f;
	}

	protected override void Step(in EffectFrame frame)
	{
		_nextMeteor -= frame.DeltaTime;

		if (_nextMeteor <= 0 && _live < Capacity)
		{
			_nextMeteor = (Random.NextSingle() * 0.8f) + 0.2f;

			_meteors[_live++] = new Meteor
			{
				X = Random.Next(frame.Width * 1.2f),
				Y = -10f,
				Speed = (Random.NextSingle() * 150f) + 150f,
				Length = (Random.NextSingle() * 14f) + 8f,
			};
		}

		for (var i = _live - 1; i >= 0; i--)
		{
			ref var meteor = ref _meteors[i];

			meteor.X -= meteor.Speed * 0.6f * frame.DeltaTime;
			meteor.Y += meteor.Speed * frame.DeltaTime;

			if (meteor.Y > frame.Height + 20)
			{
				_meteors[i] = _meteors[--_live];
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 1f * size;
		paint.Color = SKColors.White;

		_tailColors[0] = WithAlpha(Primary, 0.9f);
		_tailColors[1] = Primary.WithAlpha(0);

		for (var i = 0; i < _live; i++)
		{
			var meteor = _meteors[i];
			var length = meteor.Length * size;
			var tipX = meteor.X + (length * 0.6f);
			var tipY = meteor.Y - length;

			// The tail is a gradient rather than a fading series of segments,
			// so it stays one stroke however long it gets.
			using var shader = SKShader.CreateLinearGradient(
				new SKPoint(meteor.X, meteor.Y),
				new SKPoint(tipX, tipY),
				_tailColors,
				SKShaderTileMode.Clamp);

			paint.Shader = shader;
			canvas.DrawLine(meteor.X, meteor.Y, tipX, tipY, paint);
		}

		paint.Shader = null;
	}
}

/// <summary>A hot core throwing short-lived streaks in every direction.</summary>
[Effect("Sparkler", EffectCategory.Light,
	Cost = EffectCost.Medium,
	Description = "A hot core throwing short-lived streaks.",
	Tags = "emitter,fire,accent",
	CommonKnobs = "Count,Speed,Size,Opacity,Seed,Gravity")]
public sealed partial class Sparkler : ParticleEffect
{
	private const int Capacity = 768;

	private struct Streak
	{
		public float X;
		public float Y;
		public float PreviousX;
		public float PreviousY;
		public float VelocityX;
		public float VelocityY;
		public float Life;
	}

	private readonly Streak[] _streaks = new Streak[Capacity];
	private int _live;

	/// <summary>Streaks emitted per frame, before the device tier scales it.</summary>
	protected override int NaturalCount => 4;

	public override float TrailFade => 0.3f;

	protected override void Emit(in EffectFrame frame) => _live = 0;

	protected override void Step(in EffectFrame frame)
	{
		var gravity = (float)Gravity;
		var centerX = frame.Width / 2f;
		var centerY = frame.Height / 2f;
		var spawn = SpawnThisFrame(ResolveCount(in frame), in frame);

		for (var i = 0; i < spawn && _live < Capacity; i++)
		{
			var angle = Random.Next(Tau);
			var speed = (Random.NextSingle() * 90f) + 30f;

			_streaks[_live++] = new Streak
			{
				X = centerX,
				Y = centerY,
				PreviousX = centerX,
				PreviousY = centerY,
				VelocityX = MathF.Cos(angle) * speed,
				VelocityY = MathF.Sin(angle) * speed,
				Life = (Random.NextSingle() * 0.4f) + 0.2f,
			};
		}

		for (var i = _live - 1; i >= 0; i--)
		{
			ref var streak = ref _streaks[i];

			streak.PreviousX = streak.X;
			streak.PreviousY = streak.Y;
			streak.X += streak.VelocityX * frame.DeltaTime;
			streak.Y += streak.VelocityY * frame.DeltaTime;
			streak.VelocityY += 50f * gravity * frame.DeltaTime;
			streak.Life -= frame.DeltaTime;

			if (streak.Life <= 0)
			{
				_streaks[i] = _streaks[--_live];
			}
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();

		for (var i = 0; i < _live; i++)
		{
			var streak = _streaks[i];
			Line(canvas, streak.PreviousX, streak.PreviousY, streak.X, streak.Y,
				Accent, MathF.Max(0f, streak.Life * 2f), 0.8f * size);
		}
	}
}

/// <summary>Ragged discharges arcing between two fixed terminals.</summary>
[Effect("Arcs", EffectCategory.Light,
	Cost = EffectCost.Light,
	Description = "Ragged discharges arcing between two terminals.",
	Tags = "electric,flicker,accent",
	CommonKnobs = "Speed,Size,Opacity,Seed")]
public sealed partial class Arcs : ParticleEffect
{
	private const int ArcCount = 3;
	private const int Segments = 13;

	private readonly SKPoint[] _points = new SKPoint[ArcCount * Segments];
	private readonly float[] _alpha = new float[ArcCount];

	protected override int NaturalCount => ArcCount;

	public override float TrailFade => 0.4f;

	protected override void Emit(in EffectFrame frame) => Rebuild(in frame);

	/// <summary>
	/// Rebuilds once per tuned step, not once per frame. Every frame stacked
	/// 2.4 times more arcs into the trail at 144 fps than at 60, and ignored
	/// <see cref="ParticleEffect.Speed"/>; at exactly 1/60 s it is still every frame.
	/// </summary>
	protected override void Step(in EffectFrame frame)
	{
		if (SpawnThisFrame(1f, in frame) > 0)
		{
			Rebuild(in frame);
		}
	}

	/// <summary>
	/// The arcs are redrawn from scratch at the tuned rate; that churn is the effect.
	/// Building them in Step rather than in Draw keeps drawing free of state.
	/// </summary>
	private void Rebuild(in EffectFrame frame)
	{
		var ax = frame.Width * 0.2f;
		var bx = frame.Width * 0.8f;
		var cy = frame.Height / 2f;

		for (var arc = 0; arc < ArcCount; arc++)
		{
			_alpha[arc] = 0.3f + (Random.NextSingle() * 0.5f);
			var offset = arc * Segments;

			_points[offset] = new SKPoint(ax, cy);

			for (var i = 1; i < Segments - 1; i++)
			{
				var u = i / (float)(Segments - 1);
				var sag = (Random.NextSingle() - 0.5f) * frame.Height * 0.35f * MathF.Sin(u * MathF.PI);
				_points[offset + i] = new SKPoint(ax + ((bx - ax) * u), cy + sag);
			}

			_points[offset + Segments - 1] = new SKPoint(bx, cy);
		}
	}

	protected override void Draw(SKCanvas canvas, in EffectFrame frame)
	{
		var size = ResolveSize();
		var ax = frame.Width * 0.2f;
		var bx = frame.Width * 0.8f;
		var cy = frame.Height / 2f;

		Dot(canvas, ax, cy, 2f * size, Primary, 1f);
		Dot(canvas, bx, cy, 2f * size, Primary, 1f);

		var paint = Paint;
		paint.Style = SKPaintStyle.Stroke;
		paint.StrokeWidth = 0.8f * size;

		for (var arc = 0; arc < ArcCount; arc++)
		{
			paint.Color = WithAlpha(Accent, _alpha[arc]);
			var offset = arc * Segments;

			for (var i = 1; i < Segments; i++)
			{
				canvas.DrawLine(_points[offset + i - 1], _points[offset + i], paint);
			}
		}
	}
}
