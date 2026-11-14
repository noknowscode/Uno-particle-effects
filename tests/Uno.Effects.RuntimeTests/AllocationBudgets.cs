namespace Uno.Effects.RuntimeTests;

/// <summary>
/// Managed bytes an effect may allocate per frame once its pools exist, as
/// measured around <c>ParticleEffect.Render</c> alone. The rule is zero; each
/// exception names its reason, so raising one is a decision rather than drift.
/// </summary>
public static class AllocationBudgets
{
	/// <summary>Every effect not listed below.</summary>
	public const long Default = 0;

	/// <remarks>
	/// Each budget is the figure measured on Skia Desktop (November 2026),
	/// rounded up to the next 32 bytes. All seven are a SkiaSharp wrapper object
	/// created per frame; removing one is a change to how that effect draws.
	/// </remarks>
	private static readonly Dictionary<string, (long Bytes, string Reason)> Exceptions = new(StringComparer.OrdinalIgnoreCase)
	{
		["Sine path"] = (96, "SkiaSharp 4 paths are immutable; SKPathBuilder.Detach hands out a new SKPath each frame (measured 88 B)."),
		["Flock"] = (96, "One detached SKPath for the whole flock each frame; one per boid was 3.2 KB (measured 88 B)."),
		["Blob"] = (96, "One detached SKPath for the outline each frame (measured 88 B)."),
		["Grain"] = (224, "SKImage.FromPixelCopy wraps the regenerated noise each frame (measured 208 B)."),
		["Meteors"] = (224, "A linear-gradient SKShader per meteor tail per frame (measured 193 B)."),
		["Metaballs"] = (256, "ShaderEffect builds an SKShader with fresh uniforms each frame; HANDOFF open question (measured 240 B)."),
		["Plasma"] = (320, "ShaderEffect builds an SKShader with fresh uniforms each frame; HANDOFF open question (measured 319 B)."),
	};

	/// <summary>The budget for the effect with this catalog name.</summary>
	public static long For(string effect) =>
		Exceptions.TryGetValue(effect, out var exception) ? exception.Bytes : Default;

	/// <summary>Why the effect has the budget it has.</summary>
	public static string? ReasonFor(string effect) =>
		Exceptions.TryGetValue(effect, out var exception) ? exception.Reason : null;
}
