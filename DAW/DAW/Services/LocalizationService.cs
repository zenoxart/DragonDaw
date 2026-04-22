using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.Services;

/// <summary>
/// Simple localization service supporting German and English.
/// Provides a dictionary-based string lookup with change notification.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private string _currentLanguage = "de";

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            OnPropertyChanged(nameof(CurrentLanguage));
            // Notify all string bindings
            OnPropertyChanged(string.Empty);
        }
    }

    public string this[string key] =>
        _currentLanguage == "en"
            ? En.GetValueOrDefault(key, key)
            : De.GetValueOrDefault(key, key);

    public string Get(string key) => this[key];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    // ── German (default) ──────────────────────────────────────────────
    private static readonly Dictionary<string, string> De = new()
    {
        // Main window
        ["app.title"] = "Dragon DAW",
        ["menu.file"] = "Datei",
        ["menu.edit"] = "Bearbeiten",
        ["menu.view"] = "Ansicht",
        ["menu.options"] = "Optionen",
        ["menu.help"] = "Hilfe",
        ["transport.rewind"] = "Zum Anfang",
        ["transport.stop"] = "Stop",
        ["transport.play"] = "Abspielen",
        ["transport.pause"] = "Pause",
        ["transport.record"] = "Aufnahme",
        ["tab.playlist"] = "PLAYLIST",
        ["tab.mixer"] = "MIXER",
        ["status.ready"] = "Bereit",

        // Options window
        ["options.title"] = "Optionen — Dragon DAW",
        ["options.tab.general"] = "⚙ Allgemein",
        ["options.tab.audio"] = "🔊 Audio",
        ["options.tab.midi"] = "🎹 MIDI",
        ["options.tab.themes"] = "🎨 Themes",
        ["options.tab.project"] = "📋 Projekt",
        ["options.tab.plugins"] = "🧩 Plugins",
        ["options.tab.debug"] = "🐛 Debug",
        ["options.general.title"] = "Allgemeine Einstellungen",
        ["options.general.language"] = "Sprache:",
        ["options.general.autosave"] = "Auto-Save Intervall:",
        ["options.general.projectfolder"] = "Standard-Projektordner:",
        ["options.general.undosteps"] = "Undo-Schritte:",
        ["options.general.autosave.off"] = "Aus",
        ["options.general.undosteps.unlimited"] = "Unbegrenzt",
        ["options.audio.title"] = "Audio-Treiber",
        ["options.audio.driver"] = "Audio-Treiber:",
        ["options.audio.output"] = "Ausgabegerät:",
        ["options.audio.samplerate"] = "Sample Rate:",
        ["options.audio.buffersize"] = "Buffer Size:",
        ["options.audio.latency"] = "Latenz:",
        ["options.audio.test"] = "Audio-Treiber testen",
        ["options.midi.title"] = "MIDI-Einstellungen",
        ["options.midi.input"] = "MIDI-Eingang:",
        ["options.midi.output"] = "MIDI-Ausgang:",
        ["options.midi.rescan"] = "🔄 MIDI-Geräte neu scannen",
        ["options.midi.monitor"] = "MIDI-Monitor",
        ["options.midi.waiting"] = "Warte auf MIDI-Eingabe…",
        ["options.midi.nodevice"] = "(Kein MIDI-Gerät)",
        ["options.themes.title"] = "Erscheinungsbild",
        ["options.themes.theme"] = "Theme:",
        ["options.themes.accent"] = "Akzentfarbe:",
        ["options.themes.scale"] = "UI-Skalierung:",
        ["options.themes.preview"] = "Vorschau:",
        ["options.themes.previewsub"] = "Theme-Vorschau",
        ["options.project.title"] = "Projekt-Metadaten",
        ["options.project.name"] = "Projektname:",
        ["options.project.artist"] = "Künstler:",
        ["options.project.genre"] = "Genre:",
        ["options.project.description"] = "Beschreibung:",
        ["options.project.bpm"] = "BPM:",
        ["options.project.timesig"] = "Taktart:",
        ["options.plugins.title"] = "Installierte Plugins",
        ["options.plugins.rescan"] = "🔄 Neu scannen",
        ["options.plugins.col.name"] = "Name",
        ["options.plugins.col.category"] = "Kategorie",
        ["options.plugins.col.icon"] = "Icon",
        ["options.plugins.col.desc"] = "Beschreibung",
        ["options.debug.title"] = "Anwendungs-Logs",
        ["options.debug.clear"] = "🗑 Logs löschen",
        ["options.debug.copy"] = "📋 Kopieren",
        ["options.debug.save"] = "💾 Logs speichern...",
        ["options.cancel"] = "Abbrechen",
        ["options.apply"] = "Übernehmen",
    };

    // ── English ───────────────────────────────────────────────────────
    private static readonly Dictionary<string, string> En = new()
    {
        // Main window
        ["app.title"] = "Dragon DAW",
        ["menu.file"] = "File",
        ["menu.edit"] = "Edit",
        ["menu.view"] = "View",
        ["menu.options"] = "Options",
        ["menu.help"] = "Help",
        ["transport.rewind"] = "Rewind",
        ["transport.stop"] = "Stop",
        ["transport.play"] = "Play",
        ["transport.pause"] = "Pause",
        ["transport.record"] = "Record",
        ["tab.playlist"] = "PLAYLIST",
        ["tab.mixer"] = "MIXER",
        ["status.ready"] = "Ready",

        // Options window
        ["options.title"] = "Options — Dragon DAW",
        ["options.tab.general"] = "⚙ General",
        ["options.tab.audio"] = "🔊 Audio",
        ["options.tab.midi"] = "🎹 MIDI",
        ["options.tab.themes"] = "🎨 Themes",
        ["options.tab.project"] = "📋 Project",
        ["options.tab.plugins"] = "🧩 Plugins",
        ["options.tab.debug"] = "🐛 Debug",
        ["options.general.title"] = "General Settings",
        ["options.general.language"] = "Language:",
        ["options.general.autosave"] = "Auto-Save Interval:",
        ["options.general.projectfolder"] = "Default Project Folder:",
        ["options.general.undosteps"] = "Undo Steps:",
        ["options.general.autosave.off"] = "Off",
        ["options.general.undosteps.unlimited"] = "Unlimited",
        ["options.audio.title"] = "Audio Driver",
        ["options.audio.driver"] = "Audio Driver:",
        ["options.audio.output"] = "Output Device:",
        ["options.audio.samplerate"] = "Sample Rate:",
        ["options.audio.buffersize"] = "Buffer Size:",
        ["options.audio.latency"] = "Latency:",
        ["options.audio.test"] = "Test Audio Driver",
        ["options.midi.title"] = "MIDI Settings",
        ["options.midi.input"] = "MIDI Input:",
        ["options.midi.output"] = "MIDI Output:",
        ["options.midi.rescan"] = "🔄 Rescan MIDI Devices",
        ["options.midi.monitor"] = "MIDI Monitor",
        ["options.midi.waiting"] = "Waiting for MIDI input…",
        ["options.midi.nodevice"] = "(No MIDI device)",
        ["options.themes.title"] = "Appearance",
        ["options.themes.theme"] = "Theme:",
        ["options.themes.accent"] = "Accent Color:",
        ["options.themes.scale"] = "UI Scaling:",
        ["options.themes.preview"] = "Preview:",
        ["options.themes.previewsub"] = "Theme Preview",
        ["options.project.title"] = "Project Metadata",
        ["options.project.name"] = "Project Name:",
        ["options.project.artist"] = "Artist:",
        ["options.project.genre"] = "Genre:",
        ["options.project.description"] = "Description:",
        ["options.project.bpm"] = "BPM:",
        ["options.project.timesig"] = "Time Signature:",
        ["options.plugins.title"] = "Installed Plugins",
        ["options.plugins.rescan"] = "🔄 Rescan",
        ["options.plugins.col.name"] = "Name",
        ["options.plugins.col.category"] = "Category",
        ["options.plugins.col.icon"] = "Icon",
        ["options.plugins.col.desc"] = "Description",
        ["options.debug.title"] = "Application Logs",
        ["options.debug.clear"] = "🗑 Clear Logs",
        ["options.debug.copy"] = "📋 Copy",
        ["options.debug.save"] = "💾 Save Logs...",
        ["options.cancel"] = "Cancel",
        ["options.apply"] = "Apply",
    };
}
