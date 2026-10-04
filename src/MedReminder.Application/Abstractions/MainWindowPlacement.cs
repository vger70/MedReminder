namespace MedReminder.Application.Abstractions;

// Size, position and maximized state of the main window, restored at the
// next start. A per-profile preference stored in
// profiles\<id>\ui.settings.json next to the text size; like it, it
// belongs to one device and is not replicated by the household sync.
// X, Y, Width and Height are the normal (not maximized) bounds in screen
// pixels, so a maximized window un-maximizes to its previous size.
public sealed record MainWindowPlacement(int X, int Y, int Width, int Height, bool Maximized);
