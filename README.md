# 🐉 Dragon DAW

A modern Digital Audio Workstation built with **WPF** and **.NET 10**, featuring a dark dragon-themed UI inspired by professional DAWs like FL Studio and Ableton Live.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Desktop-blue)
![License](https://img.shields.io/badge/License-MIT-green)

---

## Features

### 🎵 Arrangement / Playlist
- **Multi-track timeline** with beat-accurate grid and zoom (0.1×–8×)
- **Drag & drop** audio files onto tracks with automatic waveform analysis
- **Clip editing** — move, resize, mute, copy/paste, and delete clips
- **Snap grid** — quarter, eighth, sixteenth note resolution or free placement
- **Playhead** with click-to-seek and drag support

### 🎚️ Mixer
- Per-channel **volume**, **pan**, **mute/solo** controls
- **Master bus** with volume slider and dB display
- **Effect rack** with 10 slots per channel
- Plugin palette (Ctrl+P) for quick effect assignment

### 📂 Audio Browser
- Built-in file browser for navigating audio samples
- Drag files directly into the playlist
- Toggle visibility via **Ansicht → Audio Browser** (Ctrl+B)

### ✂️ Edit System
- Full **undo/redo** stack (Ctrl+Z / Ctrl+Y)
- **Cut** (Ctrl+X), **Copy** (Ctrl+C), **Paste** (Ctrl+V), **Delete**
- Edit tools: Select, Draw, Paint, Slice, Resize, Zoom
- All accessible via the **Bearbeiten** menu

### 💾 Project Management
- JSON-based project format
- New / Open / Save / Save As with recent projects list
- Unsaved changes detection

### 🎨 UI / UX
- Dark theme with dragon-red accents (#C41E3A)
- Custom-styled menus, scrollbars, transport controls
- Keyboard shortcuts for all major actions
- Compact unified toolbar

---

## Keyboard Shortcuts

| Action | Shortcut |
|---|---|
| Play | Space |
| Stop | Space (while playing) |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y |
| Cut | Ctrl+X |
| Copy | Ctrl+C |
| Paste | Ctrl+V |
| Delete | Del |
| Select All | Ctrl+A |
| Toggle Audio Browser | Ctrl+B |
| Open Plugin Palette | Ctrl+P |
| Open Sampler | Ctrl+E |

---

## Architecture

```
DAW/
├── Audio/          # Audio engine, mix engine, effects
├── Commands/       # RelayCommand, AsyncRelayCommand
├── Converters/     # WPF value converters
├── Input/          # Keyboard shortcut management
├── Models/         # Data models (Track, ArrangementClip, MenuItemModel)
├── Plugins/        # Plugin system
├── Services/       # Project, audio analysis, transport services
├── ViewModels/     # MVVM ViewModels
└── Views/          # WPF Views
    ├── Arrangement/    # Playlist/timeline controls
    └── AudioBrowser/   # File browser panel
```

**Key patterns:**
- **MVVM** — strict separation of views and view models
- **Service layer** — `TransportService`, `ProjectService`, `AudioAnalysisService`
- **NAudio** for audio playback and waveform analysis
- **Command pattern** — `RelayCommand` / `AsyncRelayCommand` for all UI actions

---

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Build & Run

```bash
git clone https://github.com/zenoxart/LapisDaw.git
cd LapisDaw
dotnet run --project DAW
```

Or open `DAW.sln` in Visual Studio 2022 and press F5.

---

## License

MIT
