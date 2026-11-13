using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.Effects.Gallery.Catalog;
using Uno.Effects.Particles;

namespace Uno.Effects.Gallery;

/// <summary>
/// Capture-only: renders effects at high resolution from a JSON shot list.
/// Same fixed-timestep loop as the render tests, at any size, with optional
/// lighten-blend long exposure.
/// </summary>
public static class ArtRenderer
{
	public static void Run(string specPath, string outputDirectory)
	{
		Directory.CreateDirectory(outputDirectory);
		using var doc = JsonDocument.Parse(File.ReadAllText(specPath));

		foreach (var shot in doc.RootElement.EnumerateArray())
		{
			var name = shot.GetProperty("name").GetString()!;
			try
			{
				Render(shot, Path.Combine(outputDirectory, name + ".png"));
				Console.WriteLine($"ok   {name}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"FAIL {name}: {ex.Message}");
			}
		}
	}

	private static void Render(JsonElement shot, string path)
	{
		var w = shot.GetProperty("w").GetSingle();
		var h = shot.GetProperty("h").GetSingle();
		var scale = Get(shot, "scale", 2f);
		var frames = (int)Get(shot, "frames", 300f);
		var exposure = (int)Get(shot, "exposure", 0f);
		var blend = shot.TryGetProperty("blend", out var b) && b.GetString() == "darken" ? SKBlendMode.Darken : SKBlendMode.Lighten;
		var surfaceColor = SKColor.Parse(shot.TryGetProperty("surface", out var s) ? s.GetString()! : "#2A2A28");

		// Optional frame sequence for the animated posters: every `every`th frame
		// from `from` on, written as JPEGs to a folder named after the shot. Inside
		// the exposure window a frame is the exposure so far. With `persistence`
		// N, a frame is instead a moving long exposure: every frame fades towards
		// the surface, to 12% over N frames, and the new frame is blended on top.
		string? sequenceDirectory = null;
		int sequenceFrom = 0, sequenceEvery = 1, persistence = 0;
		if (shot.TryGetProperty("sequence", out var sequence))
		{
			sequenceDirectory = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
			Directory.CreateDirectory(sequenceDirectory);
			sequenceFrom = (int)Get(sequence, "from", 0f);
			sequenceEvery = Math.Max(1, (int)Get(sequence, "every", 1f));
			persistence = (int)Get(sequence, "persistence", 0f);
		}

		var layers = new List<ParticleEffect>();
		foreach (var layer in shot.GetProperty("layers").EnumerateArray())
		{
			layers.Add(CreateLayer(layer));
		}

		var pw = (int)MathF.Round(w * scale);
		var ph = (int)MathF.Round(h * scale);
		var info = new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Premul);

		using var surface = SKSurface.Create(info);
		using var accumulator = exposure > 0 ? SKSurface.Create(info) : null;
		accumulator?.Canvas.Clear(surfaceColor);

		// Half floats, so a fade of a fraction of a level per frame still moves.
		using var persistent = persistence > 0 ? SKSurface.Create(info.WithColorType(SKColorType.RgbaF16)) : null;
		persistent?.Canvas.Clear(surfaceColor);
		using var fadePaint = new SKPaint
		{
			ColorF = new SKColorF(surfaceColor.Red / 255f, surfaceColor.Green / 255f, surfaceColor.Blue / 255f,
				persistence > 0 ? 1f - MathF.Pow(0.12f, 1f / persistence) : 0f),
		};

		var canvas = surface.Canvas;
		canvas.Clear(surfaceColor);

		var time = 0f;
		const float dt = 1f / 60f;

		for (var frame = 0; frame <= frames; frame++)
		{
			var delta = frame == 0 ? 0f : dt;
			time += delta;

			canvas.Save();
			canvas.Scale(scale);

			Fade(canvas, w, h, layers[0].TrailFade, surfaceColor);

			foreach (var effect in layers)
			{
				var px = w * (0.5f + 0.3f * MathF.Sin(time * 0.6f));
				var py = h * (0.5f + 0.3f * MathF.Cos(time * 0.45f));
				var context = new EffectFrame(w, h, delta * (float)effect.Speed, time, px, py, false, 1f);
				effect.Render(canvas, in context, reemit: false);
			}

			canvas.Restore();

			if (accumulator is not null && frame > frames - exposure)
			{
				using var snapshot = surface.Snapshot();
				using var paint = new SKPaint { BlendMode = blend };
				accumulator.Canvas.DrawImage(snapshot, 0, 0, paint);
			}

			if (persistent is not null)
			{
				using var snapshot = surface.Snapshot();
				using var paint = new SKPaint { BlendMode = blend };
				persistent.Canvas.DrawRect(0, 0, pw, ph, fadePaint);
				persistent.Canvas.DrawImage(snapshot, 0, 0, paint);
			}

			if (sequenceDirectory is not null && frame >= sequenceFrom && (frame - sequenceFrom) % sequenceEvery == 0)
			{
				var exposing = accumulator is not null && frame > frames - exposure;
				using var still = (persistent ?? (exposing ? accumulator! : surface)).Snapshot();
				using var jpeg = still.Encode(SKEncodedImageFormat.Jpeg, 95);
				using var output = File.Create(Path.Combine(sequenceDirectory, $"{frame:D4}.jpg"));
				jpeg.SaveTo(output);
			}
		}

		using var image = (accumulator ?? surface).Snapshot();
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		using var file = File.Create(path);
		data.SaveTo(file);

		foreach (var effect in layers)
		{
			effect.Dispose();
		}
	}

	private static ParticleEffect CreateLayer(JsonElement layer)
	{
		var id = layer.GetProperty("id").GetString()!;
		var entry = EffectCatalog.Find(id) ?? throw new InvalidOperationException($"unknown effect '{id}'");
		var effect = entry.Create();

		if (layer.TryGetProperty("palette", out var palette))
		{
			foreach (var hex in palette.EnumerateArray())
			{
				var c = SKColor.Parse(hex.GetString()!);
				effect.Palette.Add(new SolidColorBrush(Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue)));
			}
		}

		if (layer.TryGetProperty("props", out var props))
		{
			foreach (var prop in props.EnumerateObject())
			{
				var property = effect.GetType().GetProperty(prop.Name)
					?? throw new InvalidOperationException($"{id} has no property {prop.Name}");
				var value = Convert.ChangeType(prop.Value.GetDouble(), property.PropertyType, CultureInfo.InvariantCulture);
				property.SetValue(effect, value);
			}
		}

		return effect;
	}

	private static void Fade(SKCanvas canvas, float w, float h, float fade, SKColor background)
	{
		if (fade >= 1f)
		{
			canvas.Clear(background);
			return;
		}

		using var paint = new SKPaint { IsAntialias = false, Color = background.WithAlpha((byte)(fade * 255)) };
		canvas.DrawRect(0, 0, w, h, paint);
	}

	private static float Get(JsonElement e, string name, float fallback) =>
		e.TryGetProperty(name, out var v) ? v.GetSingle() : fallback;
}
