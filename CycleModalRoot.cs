// Root Panel for the cycle modal. The only reason it's a subclass
// (vs. a plain Panel) is to intercept ESC. With the modal open, the
// natural reaction is "ESC closes it" — but ESC otherwise opens the
// pause menu, so without this we'd have the modal *and* the pause
// menu stacked.
//
// _UnhandledKeyInput fires for keyboard events that no Control with
// focus consumed first. Pause-menu hotkey listeners use the same
// channel, so by handling ESC here and marking the event handled we
// short-circuit the pause-menu open path.
using Godot;

namespace EnemyCycle;

public partial class CycleModalRoot : Panel
{
    public override void _Ready()
    {
        // Make sure we receive unhandled keyboard events. Defaults
        // are tree-state dependent — be explicit.
        SetProcessUnhandledKeyInput(true);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey k) return;
        if (!k.Pressed || k.Echo) return;
        if (k.Keycode != Key.Escape) return;
        QueueFree();
        GetViewport().SetInputAsHandled();
    }
}
