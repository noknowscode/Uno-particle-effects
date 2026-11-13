using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace Uno.Effects.Particles;

/// <summary>A XAML-instantiable list of effects, used as the layers of a view.</summary>
public sealed class EffectCollection : ObservableCollection<ParticleEffect>
{
	/// <summary>
	/// Clears one item at a time. A plain Reset carries no old items, so the
	/// owning view could not unsubscribe from the effects it just lost, and an
	/// effect moved to another view would keep invalidating this one.
	/// </summary>
	protected override void ClearItems()
	{
		for (var i = Count - 1; i >= 0; i--)
		{
			RemoveAt(i);
		}
	}
}

/// <summary>
/// The single control this library ships. It owns a Skia surface, the frame
/// loop and the pointer, and draws one or more <see cref="ParticleEffect"/>
/// layers onto it.
/// </summary>
/// <remarks>
/// Built on <see cref="SKCanvasElement"/>, so the same pixels are produced on
/// every target the Uno Skia renderer supports, from one code path.
/// </remarks>
[ContentProperty(Name = nameof(Effects))]
public partial class ParticleView : SKCanvasElement
{
	private const float MaxDeltaTime = 0.05f;

	/// <summary>
	/// How long a single frame may spend settling a newly emitted pool. Four
	/// milliseconds leaves the rest of a 60 fps frame intact.
	/// </summary>
	private const double WarmUpBudgetMilliseconds = 4d;

	private readonly Stopwatch _clock = new();
	private SKSurface? _surface;
	private SKPaint? _blitPaint;
	private SKPaint? _fadePaint;
	private SKFont? _diagnosticsFont;
	private SKPaint? _diagnosticsPaint;

	private int _surfaceWidth;
	private int _surfaceHeight;
	private float _surfaceScale;
	private bool _needsEmit = true;
	private int _warmUpRemaining;
	private SKColor _surfaceColor = SKColors.Transparent;
	private bool _blitPaintValid;
	private bool _rendering;
	private bool _inViewport = true;
	private long _lastTicks;

	private float _pointerX;
	private float _pointerY;
	private bool _pointerInside;

	// Diagnostics.
	private float _fps;
	private double _lastDrawMilliseconds;
	private int _lastParticleCount;

	public ParticleView()
	{
		var effects = new EffectCollection();
		effects.CollectionChanged += OnEffectsChanged;
		SetValue(EffectsProperty, effects);

		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		EffectiveViewportChanged += OnEffectiveViewportChanged;

		PointerMoved += OnPointerMoved;
		PointerEntered += OnPointerMoved;
		PointerExited += OnPointerExited;
	}

	#region Properties

	/// <summary>
	/// The effect layers, drawn back to front. This is the content property, so
	/// effects are simply the children of the view in XAML.
	/// </summary>
	public EffectCollection Effects => (EffectCollection)GetValue(EffectsProperty);

	public static readonly DependencyProperty EffectsProperty = DependencyProperty.Register(
		nameof(Effects), typeof(EffectCollection), typeof(ParticleView), new PropertyMetadata(null, OnEffectsPropertyChanged));

	/// <summary>The surface the effects are drawn onto. Transparent is honoured.</summary>
	public Brush? Surface
	{
		get => (Brush?)GetValue(SurfaceProperty);
		set => SetValue(SurfaceProperty, value);
	}

	public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
		nameof(Surface), typeof(Brush), typeof(ParticleView),
		new PropertyMetadata(null, OnStateChanged));

	/// <summary>
	/// Stops the clock without tearing anything down. Bind this to a VisualState
	/// to start and stop an effect on hover.
	/// </summary>
	public bool Paused
	{
		get => (bool)GetValue(PausedProperty);
		set => SetValue(PausedProperty, value);
	}

	public static readonly DependencyProperty PausedProperty = DependencyProperty.Register(
		nameof(Paused), typeof(bool), typeof(ParticleView), new PropertyMetadata(false, OnPausedChanged));

	/// <summary>Master switch for pointer reactions.</summary>
	public bool Interactive
	{
		get => (bool)GetValue(InteractiveProperty);
		set => SetValue(InteractiveProperty, value);
	}

	public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
		nameof(Interactive), typeof(bool), typeof(ParticleView), new PropertyMetadata(false));

	/// <summary>
	/// Renders a single settled frame and holds it. This is what makes designer
	/// thumbnails, static previews and screenshots show a real frame rather than
	/// a blank canvas or a random first tick.
	/// </summary>
	public bool FrozenFrame
	{
		get => (bool)GetValue(FrozenFrameProperty);
		set => SetValue(FrozenFrameProperty, value);
	}

	public static readonly DependencyProperty FrozenFrameProperty = DependencyProperty.Register(
		nameof(FrozenFrame), typeof(bool), typeof(ParticleView), new PropertyMetadata(false, OnFrozenFrameChanged));

	/// <summary>
	/// Frames simulated before the first visible frame, so a view never starts
	/// from an unsettled pool. Also what a frozen frame settles for.
	/// </summary>
	public int WarmUpFrames
	{
		get => (int)GetValue(WarmUpFramesProperty);
		set => SetValue(WarmUpFramesProperty, value);
	}

	public static readonly DependencyProperty WarmUpFramesProperty = DependencyProperty.Register(
		nameof(WarmUpFrames), typeof(int), typeof(ParticleView), new PropertyMetadata(90, OnStateChanged));

	/// <summary>Global time multiplier applied on top of each effect Speed.</summary>
	public double Speed
	{
		get => (double)GetValue(SpeedProperty);
		set => SetValue(SpeedProperty, value);
	}

	public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(
		nameof(Speed), typeof(double), typeof(ParticleView), new PropertyMetadata(1d));

	/// <summary>
	/// Particle budget multiplier. 0, the default, means the device tier decides;
	/// any other value overrides it. Follows the same "0 is automatic"
	/// convention as <see cref="ParticleEffect.Count"/> and
	/// <see cref="ParticleEffect.Size"/>, so an explicit 1 is a real instruction
	/// rather than an indistinguishable default.
	/// </summary>
	public double Density
	{
		get => (double)GetValue(DensityProperty);
		set => SetValue(DensityProperty, value);
	}

	public static readonly DependencyProperty DensityProperty = DependencyProperty.Register(
		nameof(Density), typeof(double), typeof(ParticleView), new PropertyMetadata(0d, OnStateChanged));

	/// <summary>
	/// The multiplier actually in use, after the device tier. Exposed so an app
	/// can show or log what it resolved to rather than having to guess.
	/// </summary>
	public double EffectiveDensity =>
		Density > 0 ? Math.Clamp(Density, 0.05, 4) : DeviceTier.Density;

	/// <summary>Gaussian blur applied to the whole composed layer stack.</summary>
	public double Blur
	{
		get => (double)GetValue(BlurProperty);
		set => SetValue(BlurProperty, value);
	}

	public static readonly DependencyProperty BlurProperty = DependencyProperty.Register(
		nameof(Blur), typeof(double), typeof(ParticleView), new PropertyMetadata(0d, OnBlitPaintChanged));

	/// <summary>How the composed layer stack blends with what is behind it.</summary>
	public SKBlendMode BlendMode
	{
		get => (SKBlendMode)GetValue(BlendModeProperty);
		set => SetValue(BlendModeProperty, value);
	}

	public static readonly DependencyProperty BlendModeProperty = DependencyProperty.Register(
		nameof(BlendMode), typeof(SKBlendMode), typeof(ParticleView),
		new PropertyMetadata(SKBlendMode.SrcOver, OnBlitPaintChanged));

	/// <summary>
	/// Overlays frames per second, particle count and draw time. The same
	/// overlay the gallery uses, available on any device.
	/// </summary>
	public bool ShowDiagnostics
	{
		get => (bool)GetValue(ShowDiagnosticsProperty);
		set => SetValue(ShowDiagnosticsProperty, value);
	}

	public static readonly DependencyProperty ShowDiagnosticsProperty = DependencyProperty.Register(
		nameof(ShowDiagnostics), typeof(bool), typeof(ParticleView), new PropertyMetadata(false));

	/// <summary>
	/// When true (the default) a system reduced-motion preference freezes the
	/// view on a settled frame instead of animating.
	/// </summary>
	public bool RespectReducedMotion
	{
		get => (bool)GetValue(RespectReducedMotionProperty);
		set => SetValue(RespectReducedMotionProperty, value);
	}

	public static readonly DependencyProperty RespectReducedMotionProperty = DependencyProperty.Register(
		nameof(RespectReducedMotion), typeof(bool), typeof(ParticleView), new PropertyMetadata(true, OnPausedChanged));

	#endregion

	/// <summary>Frames per second measured over the last second.</summary>
	public float FramesPerSecond => _fps;

	/// <summary>Milliseconds spent in the last simulate-and-draw pass.</summary>
	public double DrawMilliseconds => _lastDrawMilliseconds;

	/// <summary>Particles simulated in the last pass, across all layers.</summary>
	public int ParticleCount => _lastParticleCount;

	#region Lifecycle

	protected override Size MeasureOverride(Size availableSize)
	{
		// SKCanvasElement derives from FrameworkElement, which measures to zero.
		// Take the space offered so the view works in a StackPanel too.
		var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
		var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
		return new Size(width, height);
	}

	private void OnLoaded(object sender, RoutedEventArgs e) => UpdateClock();

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		StopClock();

		// Every one of these wraps a native allocation. Waiting for finalization
		// is what turns a board that has been filtered a few times into a
		// process holding far more native memory than it is using.
		_surface?.Dispose();
		_surface = null;
		_blitPaint?.Dispose();
		_blitPaint = null;
		_blitPaintValid = false;
		_fadePaint?.Dispose();
		_fadePaint = null;
		_diagnosticsFont?.Dispose();
		_diagnosticsFont = null;
		_diagnosticsPaint?.Dispose();
		_diagnosticsPaint = null;

		_surfaceWidth = _surfaceHeight = 0;
		_needsEmit = true;
		_warmUpRemaining = 0;
	}

	private void OnEffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
	{
		// A tile scrolled out of sight costs nothing: one of the behavioural
		// rules the recommendation asks to hold on every platform.
		//
		// Only a positively empty viewport pauses the view. Not every head
		// implements every member of these args, and the safe answer to "I do
		// not know whether this is on screen" is to keep drawing.
		bool visible;
		try
		{
			var viewport = args.EffectiveViewport;
			visible = viewport.Width > 0 && viewport.Height > 0;
		}
		catch (NotImplementedException)
		{
			visible = true;
		}

		if (visible != _inViewport)
		{
			_inViewport = visible;
			UpdateClock();
		}
	}

	private bool ShouldRun =>
		IsLoaded
		&& _inViewport
		&& !Paused
		&& !FrozenFrame
		&& !(RespectReducedMotion && ReducedMotion.IsEnabled);

	private void UpdateClock()
	{
		if (ShouldRun)
		{
			StartClock();
		}
		else
		{
			StopClock();
			Invalidate();
		}
	}

	private void StartClock()
	{
		if (_rendering)
		{
			return;
		}

		_rendering = true;
		_clock.Restart();
		_lastTicks = 0;
		CompositionTarget.Rendering += OnRendering;
	}

	private void StopClock()
	{
		if (!_rendering)
		{
			return;
		}

		_rendering = false;
		CompositionTarget.Rendering -= OnRendering;
		_clock.Stop();
	}

	private void OnRendering(object? sender, object e) => Invalidate();

	#endregion

	#region Pointer

	private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
	{
		var point = e.GetCurrentPoint(this).Position;
		_pointerX = (float)point.X;
		_pointerY = (float)point.Y;
		_pointerInside = true;
	}

	private void OnPointerExited(object sender, PointerRoutedEventArgs e) => _pointerInside = false;

	#endregion

	#region Effects plumbing

	private static void OnEffectsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var view = (ParticleView)d;

		if (e.OldValue is EffectCollection old)
		{
			old.CollectionChanged -= view.OnEffectsChanged;
			foreach (var effect in old)
			{
				effect.Invalidated -= view.OnEffectInvalidated;
			}
		}

		if (e.NewValue is EffectCollection added)
		{
			added.CollectionChanged += view.OnEffectsChanged;
			foreach (var effect in added)
			{
				effect.Invalidated += view.OnEffectInvalidated;
			}
		}

		view._needsEmit = true;
		view.Invalidate();
	}

	private void OnEffectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		foreach (var effect in e.OldItems?.OfType<ParticleEffect>() ?? [])
		{
			effect.Invalidated -= OnEffectInvalidated;
		}

		foreach (var effect in e.NewItems?.OfType<ParticleEffect>() ?? [])
		{
			effect.Invalidated += OnEffectInvalidated;
		}

		_needsEmit = true;
		Invalidate();
	}

	private void OnEffectInvalidated(object? sender, EffectInvalidation kind)
	{
		if (kind == EffectInvalidation.State)
		{
			_needsEmit = true;
		}

		Invalidate();
	}

	private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var view = (ParticleView)d;
		view.UpdateSurfaceColor();
		view._needsEmit = true;
		view.Invalidate();
	}

	private static void OnPausedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
		((ParticleView)d).UpdateClock();

	private static void OnFrozenFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var view = (ParticleView)d;
		view._needsEmit = true;
		view.UpdateClock();
	}

	private static void OnBlitPaintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var view = (ParticleView)d;
		view._blitPaintValid = false;
		view.Invalidate();
	}

	#endregion

	#region Rendering

	protected override void RenderOverride(SKCanvas canvas, Size area)
	{
		var width = (float)area.Width;
		var height = (float)area.Height;
		if (width <= 0 || height <= 0)
		{
			return;
		}

		var scale = canvas.TotalMatrix.ScaleX;
		if (scale <= 0)
		{
			scale = 1f;
		}

		var surface = EnsureSurface(width, height, scale);
		if (surface is null)
		{
			return;
		}

		var start = Stopwatch.GetTimestamp();
		var offscreen = surface.Canvas;

		// A theme switch or a ColorAnimation changes a brush's colour in place,
		// which raises no property change here. Reading a handful of colours a
		// frame is cheap; holding callbacks on app-lifetime brushes would leak.
		UpdateSurfaceColor();
		var layers = Effects;
		for (var i = 0; i < layers.Count; i++)
		{
			layers[i].RefreshPalette();
		}

		offscreen.Save();
		offscreen.Scale(scale);

		if (_needsEmit)
		{
			_needsEmit = false;
			offscreen.Clear(SurfaceColor);

			foreach (var effect in Effects)
			{
				effect.MarkUnemitted();
				effect.ElapsedTime = 0f;
			}

			_warmUpRemaining = Math.Max(0, WarmUpFrames);

			if (_warmUpRemaining == 0)
			{
				RenderPass(offscreen, width, height, 0f);
			}
		}

		if (_warmUpRemaining > 0)
		{
			WarmUp(offscreen, width, height);
		}
		else
		{
			var delta = NextDeltaTime();

			// A repaint that advances no time must not touch the surface: on a
			// trail effect, re-fading the same frame would slowly wash a paused
			// tile out to nothing.
			if (delta > 0)
			{
				RenderPass(offscreen, width, height, delta);
			}
		}

		offscreen.Restore();

		Blit(canvas, surface, scale);

		_lastDrawMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;

		if (ShowDiagnostics)
		{
			DrawDiagnostics(canvas, width, height);
		}
	}

	/// <summary>
	/// Settles the pool a few frames at a time, within a budget, continuing on
	/// later frames until it is done.
	/// </summary>
	/// <remarks>
	/// Settling is not optional — an unsettled first frame is why the study
	/// sheet warms up at all — but doing all of it in one go is what turns a
	/// board of tiles into a stall. Measured on the 64-tile board, the whole
	/// warm-up came to about 2.5 seconds of work, and the single worst tile
	/// (Metaballs) held the UI thread for 226 ms on its own. Spread over a few
	/// frames the tile visibly settles instead, and nothing blocks.
	/// </remarks>
	private void WarmUp(SKCanvas canvas, float width, float height)
	{
		var deadline = Stopwatch.GetTimestamp() + (long)(WarmUpBudgetMilliseconds / 1000d * Stopwatch.Frequency);

		// Always at least one frame, so a single expensive effect still makes
		// progress rather than spinning forever on an exhausted budget.
		do
		{
			RenderPass(canvas, width, height, 1f / 60f);
			_warmUpRemaining--;
		}
		while (_warmUpRemaining > 0 && Stopwatch.GetTimestamp() < deadline);

		if (_warmUpRemaining > 0)
		{
			// Ask for another frame: the clock may be stopped, as it is for
			// every idle tile on the board.
			Invalidate();
		}
		else
		{
			// Do not charge the time spent settling to the simulation.
			_lastTicks = _clock.ElapsedTicks;
		}
	}

	private float NextDeltaTime()
	{
		if (!_rendering)
		{
			return 0f;
		}

		var ticks = _clock.ElapsedTicks;
		var delta = (float)((ticks - _lastTicks) / (double)Stopwatch.Frequency);
		_lastTicks = ticks;

		if (delta <= 0f)
		{
			return 0f;
		}

		_fps = _fps <= 0 ? 1f / delta : _fps + (1f / delta - _fps) * 0.1f;

		return Math.Min(delta, MaxDeltaTime) * (float)Speed;
	}

	private void RenderPass(SKCanvas canvas, float width, float height, float delta)
	{
		var effects = Effects;
		if (effects.Count == 0)
		{
			canvas.Clear(SurfaceColor);
			_lastParticleCount = 0;
			return;
		}

		FadeSurface(canvas, width, height, effects[0].TrailFade);

		var density = (float)EffectiveDensity;
		var total = 0;

		// Indexed rather than foreach: Collection<T> hands out a boxed
		// enumerator, which would be an allocation every frame.
		for (var i = 0; i < effects.Count; i++)
		{
			var effect = effects[i];
			var effectDelta = delta * (float)effect.Speed;
			effect.ElapsedTime += effectDelta;

			var interactive = Interactive && effect.Interactive;
			var inside = interactive && _pointerInside;

			// Off-pointer, the view supplies a slow synthetic drift so an
			// interactive effect still reads as alive in a grid of tiles.
			var pointerX = inside ? _pointerX : width * (0.5f + 0.3f * MathF.Sin(effect.ElapsedTime * 0.6f));
			var pointerY = inside ? _pointerY : height * (0.5f + 0.3f * MathF.Cos(effect.ElapsedTime * 0.45f));

			var frame = new EffectFrame(
				width, height, effectDelta, effect.ElapsedTime, pointerX, pointerY, inside, density);

			effect.Render(canvas, in frame, reemit: false);

			if (effect.SimulatesParticles)
			{
				total += effect.ResolveCount(in frame);
			}
		}

		_lastParticleCount = total;
	}

	private void FadeSurface(SKCanvas canvas, float width, float height, float fade)
	{
		var background = SurfaceColor;

		if (fade >= 1f)
		{
			canvas.Clear(background);
			return;
		}

		var paint = _fadePaint ??= new SKPaint { IsAntialias = false };

		if (background.Alpha == 0)
		{
			// Nothing to fade towards, so erase a fraction of the alpha instead.
			paint.Color = SKColors.Black.WithAlpha((byte)(fade * 255));
			paint.BlendMode = SKBlendMode.DstOut;
		}
		else
		{
			paint.Color = background.WithAlpha((byte)(fade * 255));
			paint.BlendMode = SKBlendMode.SrcOver;
		}

		canvas.DrawRect(0, 0, width, height, paint);
	}

	private SKSurface? EnsureSurface(float width, float height, float scale)
	{
		var pixelWidth = Math.Max(1, (int)MathF.Round(width * scale));
		var pixelHeight = Math.Max(1, (int)MathF.Round(height * scale));

		if (_surface is not null
			&& _surfaceWidth == pixelWidth
			&& _surfaceHeight == pixelHeight
			&& Math.Abs(_surfaceScale - scale) < 0.001f)
		{
			return _surface;
		}

		_surface?.Dispose();
		_surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
		_surfaceWidth = pixelWidth;
		_surfaceHeight = pixelHeight;
		_surfaceScale = scale;
		_needsEmit = true;

		return _surface;
	}

	private void Blit(SKCanvas canvas, SKSurface surface, float scale)
	{
		if (!_blitPaintValid)
		{
			_blitPaint?.Dispose();
			_blitPaint = null;

			if (Blur > 0 || BlendMode != SKBlendMode.SrcOver)
			{
				_blitPaint = new SKPaint { BlendMode = BlendMode };
				if (Blur > 0)
				{
					var sigma = (float)Blur * scale;
					_blitPaint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma);
				}
			}

			_blitPaintValid = true;
		}

		canvas.Save();
		canvas.Scale(1f / scale);
		surface.Draw(canvas, 0, 0, _blitPaint);
		canvas.Restore();
	}

	/// <summary>
	/// The surface colour, resolved once per frame rather than at each of its
	/// reads.
	/// </summary>
	private SKColor SurfaceColor => _surfaceColor;

	private void UpdateSurfaceColor() =>
		_surfaceColor = Surface is SolidColorBrush solid
			? new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A)
			: SKColors.Transparent;

	private void DrawDiagnostics(SKCanvas canvas, float width, float height)
	{
		_diagnosticsFont ??= new SKFont(SKTypeface.Default, 10f);
		_diagnosticsPaint ??= new SKPaint { IsAntialias = true };

		var text = _lastParticleCount > 0
			? $"{_fps:0} fps · {_lastParticleCount} particles · {_lastDrawMilliseconds:0.00} ms"
			: $"{_fps:0} fps · {_lastDrawMilliseconds:0.00} ms";

		_diagnosticsPaint.Color = SKColors.Black.WithAlpha(140);
		canvas.DrawRect(6, height - 22, _diagnosticsFont.MeasureText(text) + 12, 16, _diagnosticsPaint);

		_diagnosticsPaint.Color = new SKColor(0xE9, 0xE5, 0xDD);
		canvas.DrawText(text, 12, height - 10, SKTextAlign.Left, _diagnosticsFont, _diagnosticsPaint);
	}

	#endregion
}
