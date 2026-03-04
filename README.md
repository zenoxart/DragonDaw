# LapisDaw

LapisDaw is a Digital Audio Workstation (DAW) built with WPF and .NET 10.

## Features

- Arrangement view for composing and editing audio tracks
- Mixer with channel controls
- Audio browser for managing sound files
- Interactive time marker for precise navigation
- Audio effects and plugin support (via NAudio)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (Windows)
- Windows OS (WPF application)

## Getting Started

```bash
# Clone the repository
git clone https://github.com/zenoxart/LapisDaw.git
cd LapisDaw

# Build the project
dotnet build DAW/DAW.slnx

# Run the application
dotnet run --project DAW/DAW/DAW.csproj

# Run tests
dotnet test DAW/DAW.Tests/DAW.Tests.csproj
```

## Project Structure

```
DAW/
├── DAW/                # Main WPF application
│   ├── Audio/          # Audio engine and effects
│   ├── Commands/       # WPF commands
│   ├── Controls/       # Custom WPF controls
│   ├── Converters/     # Value converters
│   ├── Models/         # Data models
│   ├── Plugins/        # Plugin system
│   ├── Services/       # Application services
│   ├── ViewModels/     # MVVM view models
│   └── Views/          # WPF views (Arrangement, Mixer, AudioBrowser)
├── DAW.Tests/          # Unit tests
└── DAW.slnx            # Solution file
```

## License

This project is proprietary. All rights reserved.
