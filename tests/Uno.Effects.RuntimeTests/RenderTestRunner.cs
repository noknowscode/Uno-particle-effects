using System.Diagnostics;
using System.Security.Cryptography;
using SkiaSharp;
using Uno.Effects.Particles;

namespace Uno.Effects.RuntimeTests;

/// <summary>The hash of one sampled frame.</summary>
public sealed record FrameHash(int Frame, string Hash);

/// <summary>
/// Pixel statistics of one frame that should not depend on the frame rate: the
/// share of pixels visibly off the surface colour, and the mean distance from
/// it (0 is the bare surface, 1 is every channel at the far end).
/// </summary>
public sealed record FrameStatistics(double Coverage, double Intensity);

/// <summary>What one effect did under the harness.</summary>
public sealed record RenderTestResult(
	string Effect,
	bool Deterministic,
	bool DrewSomething,
	IReadOnlyList<FrameHash> Frames,
	long BytesPerFrame,
	long AllocationBudget,
	double MedianFrameMilliseconds,
	FrameStatistics? At30,
	FrameStatistics? At60,
	FrameStatistics? At144,
	string? Error)
{
	public bool WithinBudget => BytesPerFrame <= AllocationBudget;

	/// <summary>
	/// The 144 fps run ends where the 60 fps run does. 30 fps is reported, not
	/// asserted: there an effect draws once per two tuned steps, so a trail is
	/// stamped half as often and reads dimmer. Closing that needs sub-stepping.
	/// </summary>
	public bool FrameRateIndependent =>
		At60 is not null && At144 is not null && RenderTestRunner.Matches(At144, At60);

	public bool Passed => Error is null && Deterministic && DrewSomething && WithinBudget && FrameRateIndependent;
}

/// <summary>
/// Renders each effect at a fixed timestep and hashes the sampled frames, then
/// checks the frame-rate independence and allocation budget of each.
/// </summary>
/// <remarks>
/// This is the foundation the rest of the test story rests on. Because an
/// effect is driven only by its <see cref="ParticleEffect.Seed"/> and by the
/// deltas it is handed, the same seed and the same fixed timestep must produce
/// byte-identical frames — first between two runs in one process, and then
/// between heads, by comparing the hashes this produces on each.
///
/// Different frame rates cannot produce the same pixels, since random numbers
/// are drawn per step, so that check compares statistics of the frame at the
/// same moment instead.
///
/// Nothing here touches the visual tree, so the harness runs anywhere the
/// effects assembly runs.
/// </remarks>
public static class RenderTestRunner
{
	/// <summary>The frames compared, matching the recommendation.</summary>
	public static readonly int[] SampledFrames = [0, 60, 300];

	public const int Size = 256;

	/// <summary>
	/// How far a statistic at 144 fps may sit from the 60 fps value: 25% of it,
	/// or the floor when the effect is too sparse for a share to mean anything.
	/// Measured with the fixes in place, every effect but the two with known
	/// frame-rate bugs (Ink, Arcs) sat within 13%.
	/// </summary>
	public const double RelativeTolerance = 0.25;

	private const double CoverageFloor = 0.005;
	private const double IntensityFloor = 0.002;

	/// <summary>A pixel counts as covered past this summed RGB distance.</summary>
	private const int CoveredThreshold = 24;

	private const float FixedDelta = 1f / 60f;

	/// <summary>The surface colour frames are rendered onto.</summary>
	private static readonly SKColor Surface = new(0x2A, 0x2A, 0x28);

	/// <summary>The hash of a surface that has been cleared and nothing else.</summary>
	private static readonly string BlankHash = ComputeBlankHash();

	private static string ComputeBlankHash()
	{
		using var surface = CreateSurface();
		return Hash(surface);
	}

	/// <summary>The simulated time the frame-rate check compares at: frame 300 at 1/60 s.</summary>
	public static float ComparedSeconds => SampledFrames.Max() * FixedDelta;

	/// <summary>Runs every effect and returns one result each.</summary>
	public static IReadOnlyList<RenderTestResult> RunAll(
		IReadOnlyList<(string Name, Func<ParticleEffect> Factory)> effects,
		string? outputDirectory = null)
	{
		var results = new List<RenderTestResult>(effects.Count);

		foreach (var (name, factory) in effects)
		{
			results.Add(Run(name, factory, outputDirectory));
		}

		return results;
	}

	/// <summary>
	/// Runs one effect twice at 60 fps and compares the two runs, then once each
	/// at 30 and 144 fps to compare against the first.
	/// </summary>
	public static RenderTestResult Run(
		string name,
		Func<ParticleEffect> factory,
		string? outputDirectory = null,
		bool checkFrameRates = true)
	{
		var budget = AllocationBudgets.For(name);

		try
		{
			var first = Drive(factory, FixedDelta, outputDirectory, name);
			var second = Drive(factory, FixedDelta, null, null);

			var deterministic = first.Hashes.Count == second.Hashes.Count;
			for (var i = 0; deterministic && i < first.Hashes.Count; i++)
			{
				deterministic = first.Hashes[i].Hash == second.Hashes[i].Hash;
			}

			// An emitter draws nothing on frame 0, which is correct; an effect
			// that draws nothing on any sampled frame is broken. Without this,
			// a silently dead effect passes on identical blank frames.
			var drewSomething = first.Hashes.Any(f => f.Hash != BlankHash);

			var at30 = checkFrameRates ? Drive(factory, 1f / 30f, null, null).Last : null;
			var at144 = checkFrameRates ? Drive(factory, 1f / 144f, null, null).Last : first.Last;

			return new RenderTestResult(
				name, deterministic, drewSomething, first.Hashes, first.BytesPerFrame, budget,
				first.MedianMilliseconds, at30, first.Last, at144, null);
		}
		catch (Exception ex)
		{
			return new RenderTestResult(name, false, false, [], 0, budget, 0, null, null, null, ex.Message);
		}
	}

	/// <summary>Whether a statistic taken at another frame rate matches the 60 fps one.</summary>
	public static bool Matches(FrameStatistics other, FrameStatistics at60) =>
		Within(other.Coverage, at60.Coverage, CoverageFloor)
		&& Within(other.Intensity, at60.Intensity, IntensityFloor);

	private static bool Within(double value, double reference, double floor) =>
		Math.Abs(value - reference) <= Math.Max(reference * RelativeTolerance, floor);

	private sealed record DriveResult(
		List<FrameHash> Hashes,
		long BytesPerFrame,
		double MedianMilliseconds,
		FrameStatistics Last);

	/// <summary>
	/// Drives a fresh effect for <see cref="ComparedSeconds"/> at a fixed
	/// <paramref name="delta"/>, hashing the sampled frames when it is the
	/// 60 fps run, and measures the last frame.
	/// </summary>
	private static DriveResult Drive(
		Func<ParticleEffect> factory,
		float delta,
		string? outputDirectory,
		string? name)
	{
		using var effect = factory();
		using var surface = CreateSurface();

		var canvas = surface.Canvas;
		var last = (int)MathF.Round(ComparedSeconds / delta);
		var sampleHashes = delta == FixedDelta;
		var hashes = new List<FrameHash>(SampledFrames.Length);
		var timings = new List<double>(last + 1);

		long allocated = 0;
		var time = 0f;
		var fadeCarry = 0f;

		for (var frame = 0; frame <= last; frame++)
		{
			// Frame 0 is the emitted state before any time has passed.
			var frameDelta = frame == 0 ? 0f : delta;
			time += frameDelta;

			// The same synthetic pointer the view supplies when nothing is
			// hovering, so interactive effects are exercised too.
			var pointerX = Size * (0.5f + (0.3f * MathF.Sin(time * 0.6f)));
			var pointerY = Size * (0.5f + (0.3f * MathF.Cos(time * 0.45f)));

			var context = new EffectFrame(Size, Size, frameDelta, time, pointerX, pointerY, false, 1f);

			var start = Stopwatch.GetTimestamp();

			Fade(canvas, effect.TrailFade, frameDelta, ref fadeCarry);

			// Only the effect is measured, so the harness's own fade paint,
			// hashing and PNG writes are not charged to it.
			var before = GC.GetAllocatedBytesForCurrentThread();
			effect.Render(canvas, in context, reemit: false);
			var churn = GC.GetAllocatedBytesForCurrentThread() - before;

			timings.Add((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);

			// From frame 2 on, after the pools exist, so it reports
			// steady-state churn rather than the one-off cost of emitting.
			if (frame >= 2)
			{
				allocated += churn;
			}

			if (sampleHashes && Array.IndexOf(SampledFrames, frame) >= 0)
			{
				hashes.Add(new FrameHash(frame, Hash(surface)));
				Save(surface, outputDirectory, name, frame);
			}
		}

		timings.Sort();

		return new DriveResult(
			hashes, allocated / Math.Max(1, last - 1), timings[timings.Count / 2], Measure(surface));
	}

	private static SKSurface CreateSurface()
	{
		var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
		surface.Canvas.Clear(Surface);
		return surface;
	}

	private static FrameStatistics Measure(SKSurface surface)
	{
		using var pixmap = surface.PeekPixels();
		var pixels = pixmap.GetPixelSpan();

		long covered = 0;
		long distance = 0;

		for (var i = 0; i < pixels.Length; i += 4)
		{
			var diff = Math.Abs(pixels[i] - Surface.Red)
				+ Math.Abs(pixels[i + 1] - Surface.Green)
				+ Math.Abs(pixels[i + 2] - Surface.Blue);

			if (diff > CoveredThreshold)
			{
				covered++;
			}

			distance += diff;
		}

		var count = (double)Size * Size;
		return new FrameStatistics(covered / count, distance / (count * 765d));
	}

	private static void Fade(SKCanvas canvas, float trailFade, float deltaTime, ref float carry)
	{
		if (trailFade >= 1f)
		{
			canvas.Clear(Surface);
			return;
		}

		// The same 60 Hz cadence ParticleView.FadeSurface keeps (see
		// ParticleView.FadeOverFrame): whole 1/60 s steps only.
		carry += deltaTime * 60f;
		var steps = (int)(carry + 0.0001f);
		carry = MathF.Max(0f, carry - steps);

		if (steps == 0)
		{
			return;
		}

		trailFade = steps == 1 ? trailFade : 1f - MathF.Pow(1f - trailFade, steps);

		using var paint = new SKPaint
		{
			IsAntialias = false,
			Color = Surface.WithAlpha((byte)(trailFade * 255)),
		};

		canvas.DrawRect(0, 0, Size, Size, paint);
	}

	private static string Hash(SKSurface surface)
	{
		using var pixmap = surface.PeekPixels();
		return Convert.ToHexString(SHA256.HashData(pixmap.GetPixelSpan()))[..16];
	}

	private static void Save(SKSurface surface, string? directory, string? name, int frame)
	{
		if (directory is null || name is null)
		{
			return;
		}

		Directory.CreateDirectory(directory);

		using var image = surface.Snapshot();
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		using var file = File.Create(Path.Combine(directory, $"{Slug(name)}-{frame:000}.png"));
		data.SaveTo(file);
	}

	private static string Slug(string name) =>
		string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-'));
}
