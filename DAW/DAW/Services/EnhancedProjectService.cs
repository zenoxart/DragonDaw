using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAW.Models;
using DAW.ViewModels;
using DAW.Audio.Effects;

namespace DAW.Services;

/// <summary>
/// Enhanced project service for managing DAW project files with JSON serialization.
/// Handles complete project state including tracks, clips, mixer settings, effects, and automation.
/// File extension: .dawproj (JSON-based format)
/// </summary>
public sealed class EnhancedProjectService
{
    private readonly FileSystemService _fileSystemService;
    private readonly SettingsService _settingsService;
    private readonly JsonSerializerOptions _jsonOptions;

    private string? _currentProjectPath;
    private DawProject? _currentProject;
    private bool _hasUnsavedChanges;
    private DateTime? _sessionStartTime;

    public EnhancedProjectService(FileSystemService fileSystemService, SettingsService settingsService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        // Configure JSON serialization
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    #region Properties

    public DawProject? CurrentProject 
    { 
        get => _currentProject;
        set => _currentProject = value;
    }
    
    public string? CurrentProjectPath 
    { 
        get => _currentProjectPath;
        set => _currentProjectPath = value;
    }
    
    public bool HasUnsavedChanges => _hasUnsavedChanges;
    
    public string CurrentProjectDisplayName
    {
        get
        {
            if (_currentProject == null) return "No Project";
            if (string.IsNullOrEmpty(_currentProjectPath))
                return $"{_currentProject.ProjectName}*";
            
            var fileName = Path.GetFileNameWithoutExtension(_currentProjectPath);
            return _hasUnsavedChanges ? $"{fileName}*" : fileName;
        }
    }

    #endregion

    #region Events

    public event EventHandler<ProjectLoadedEventArgs>? ProjectLoaded;
    public event EventHandler<ProjectSavedEventArgs>? ProjectSaved;
    public event EventHandler<bool>? UnsavedChangesChanged;

    #endregion

    #region Public Methods

    public async Task<DawProject> CreateNewProjectAsync(string projectName = "New Project")
    {
        _sessionStartTime = DateTime.Now;
        
        var project = new DawProject
        {
            ProjectName = projectName,
            CreatedDate = DateTime.Now,
            LastModifiedDate = DateTime.Now,
            Author = Environment.UserName,
            History = new ProjectHistory
            {
                SessionStartTime = _sessionStartTime,
                ChangeLog = [new ProjectChangeEntry
                {
                    Timestamp = DateTime.Now,
                    ChangeType = "Created",
                    Description = $"Projekt '{projectName}' erstellt"
                }]
            }
        };

        _currentProject = project;
        _currentProjectPath = null;
        SetUnsavedChanges(true);

        ProjectLoaded?.Invoke(this, new ProjectLoadedEventArgs(project, null));
        return project;
    }

    public async Task<DawProject> OpenProjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Project file not found: {filePath}");

        _sessionStartTime = DateTime.Now;
        
        var jsonContent = await File.ReadAllTextAsync(filePath);
        var project = JsonSerializer.Deserialize<DawProject>(jsonContent, _jsonOptions);

        if (project == null)
            throw new InvalidOperationException("Failed to deserialize project");

        // Update history
        project.History.SessionStartTime = _sessionStartTime;
        project.History.OpenHistory.Add(DateTime.Now);
        project.FilePath = filePath;
        
        _currentProject = project;
        _currentProjectPath = filePath;
        SetUnsavedChanges(false);

        ProjectLoaded?.Invoke(this, new ProjectLoadedEventArgs(project, filePath));
        return project;
    }

    public async Task<bool> SaveProjectAsync()
    {
        if (_currentProject == null) return false;

        if (string.IsNullOrEmpty(_currentProjectPath))
            return await SaveProjectAsAsync();

        return await SaveToPathAsync(_currentProjectPath);
    }

    public async Task<bool> SaveProjectAsAsync()
    {
        if (_currentProject == null) return false;

        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DAW Projects",
            $"{_currentProject.ProjectName}.dawproj");

        var directory = Path.GetDirectoryName(defaultPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        return await SaveToPathAsync(defaultPath);
    }

    private async Task<bool> SaveToPathAsync(string filePath)
    {
        if (_currentProject == null) return false;

        try
        {
            _currentProject.LastModifiedDate = DateTime.Now;
            _currentProject.FilePath = filePath;
            
            // Update history
            _currentProject.History.SaveHistory.Add(DateTime.Now);
            if (_sessionStartTime.HasValue)
            {
                var sessionDuration = (DateTime.Now - _sessionStartTime.Value).TotalMinutes;
                _currentProject.History.TotalEditingTimeMinutes += sessionDuration;
                _sessionStartTime = DateTime.Now; // Reset for next save interval
            }
            
            var jsonContent = JsonSerializer.Serialize(_currentProject, _jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonContent);

            _currentProjectPath = filePath;
            SetUnsavedChanges(false);

            ProjectSaved?.Invoke(this, new ProjectSavedEventArgs(_currentProject, filePath));
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save project: {ex.Message}", ex);
        }
    }

    public void MarkAsModified()
    {
        SetUnsavedChanges(true);
    }
    
    /// <summary>
    /// Records a change in the project history
    /// </summary>
    public void RecordChange(string changeType, string description, string? affectedElement = null, string? oldValue = null, string? newValue = null)
    {
        if (_currentProject == null) return;
        
        _currentProject.History.ChangeLog.Add(new ProjectChangeEntry
        {
            Timestamp = DateTime.Now,
            ChangeType = changeType,
            Description = description,
            AffectedElement = affectedElement,
            OldValue = oldValue,
            NewValue = newValue
        });
        
        SetUnsavedChanges(true);
    }

    public DawProject ExportCurrentState(MainViewModel mainViewModel)
    {
        var project = _currentProject ?? new DawProject();

        // Export global settings
        project.Settings.BPM = mainViewModel.BPM;
        project.Settings.PlaybackPosition = mainViewModel.CurrentPosition;
        
        // Export master channel
        project.MasterChannel.Volume = mainViewModel.MasterVolume;
        project.MasterChannel.Pan = mainViewModel.MasterPan;
        
        // Export master effects
        project.MasterChannel.Effects.Clear();
        foreach (var slot in mainViewModel.MasterEffectSlots.Where(s => s.HasEffect))
        {
            if (slot.Effect != null)
            {
                var projectEffect = ExportEffect(slot.Effect, slot.SlotNumber);
                project.MasterChannel.Effects.Add(projectEffect);
            }
        }

        // Export tracks
        project.Tracks.Clear();
        project.Files.Clear();
        project.MixerChannelLayout.Clear();

        // Export mixer channel display order and routing
        for (int i = 0; i < mainViewModel.MixerChannels.Count; i++)
        {
            var ch = mainViewModel.MixerChannels[i];
            project.MixerChannelLayout.Add(new ProjectMixerChannelData
            {
                ChannelNumber  = ch.ChannelNumber,
                DisplayOrder   = i,
                SourceTrackId  = ch.SourceTrack?.TrackNumber.ToString(),
                SendTargets    = ch.SendTargets.ToList()
            });
        }
        
        foreach (var track in mainViewModel.Tracks)
        {
            var projectTrack = new ProjectTrack
            {
                Id = track.TrackNumber.ToString(),
                TrackNumber = track.TrackNumber,
                Title = track.Title,
                Artist = track.Artist,
                FilePath = track.FilePath ?? string.Empty,
                Color = new ProjectColor(track.ChannelColor),
                Volume = track.Volume,
                Pan = track.Pan,
                IsMuted = track.IsMuted,
                IsSolo = track.IsSolo,
                IsArmed = track.IsArmed
            };
            
            // Export track effects from slots (preserves slot positions)
            foreach (var slot in track.EffectSlots.Where(s => s.HasEffect))
            {
                if (slot.Effect != null)
                {
                    var projectEffect = ExportEffect(slot.Effect, slot.SlotNumber);
                    projectTrack.Effects.Add(projectEffect);
                }
            }

            // Export clips
            var arrangementTrack = mainViewModel.ArrangementVm.Tracks
                .FirstOrDefault(t => t.Model == track);

            if (arrangementTrack != null)
            {
                foreach (var clipVm in arrangementTrack.Clips)
                {
                    var projectClip = new ProjectClip
                    {
                        Id = clipVm.Model.Id,
                        DisplayName = clipVm.DisplayName,
                        StartBeat = clipVm.Model.StartBeat,
                        LengthInBeats = clipVm.Model.LengthInBeats,
                        SourceFilePath = clipVm.Model.SourceFilePath,
                        Color = new ProjectColor(clipVm.Color),
                        WaveformData = clipVm.WaveformData ?? []
                    };
                    projectTrack.Clips.Add(projectClip);
                    
                    // Add to file references
                    if (!string.IsNullOrEmpty(clipVm.Model.SourceFilePath))
                    {
                        AddFileReference(project, clipVm.Model.SourceFilePath);
                    }
                }
            }
            
            // Add track file to references
            if (!string.IsNullOrEmpty(track.FilePath))
            {
                AddFileReference(project, track.FilePath);
            }

            project.Tracks.Add(projectTrack);
        }

        // ── Export patterns (Channel Rack + Piano Roll) ───────────────────
        project.Patterns.Clear();
        project.ActivePatternName = mainViewModel.PatternVm.ActivePattern?.Name;

        foreach (var pattern in mainViewModel.PatternVm.AllPatterns)
        {
            var pp = new ProjectPattern
            {
                Name      = pattern.Name,
                StepCount = pattern.StepCount,
                Swing     = pattern.Swing
            };

            foreach (var ch in pattern.Channels)
            {
                var pc = new ProjectPatternChannel
                {
                    Name         = ch.Name,
                    SamplePath   = ch.SamplePath,
                    PluginIcon   = ch.PluginIcon,
                    ChannelColor = ch.ChannelColor.ToString(),
                    IsMuted      = ch.IsMuted,
                    MixerTrack   = ch.MixerTrack,
                    Volume       = ch.Volume,
                    Steps        = ch.Steps.Select(s => new ProjectStep
                    {
                        IsActive = s.IsActive,
                        Velocity = s.Velocity,
                        Pan      = s.Pan,
                        Pitch    = s.Pitch
                    }).ToList(),
                    PianoRollNotes = ch.PianoRollNotes.Select(n => new ProjectPianoRollNote
                    {
                        Pitch     = n.Pitch,
                        StartTick = n.StartTick,
                        Length    = n.Length,
                        Velocity  = n.Velocity,
                        Pan       = n.Pan,
                        Release   = n.Release,
                        IsMuted   = n.IsMuted
                    }).ToList()
                };

                if (!string.IsNullOrEmpty(ch.SamplePath))
                    AddFileReference(project, ch.SamplePath);

                pp.Channels.Add(pc);
            }

            project.Patterns.Add(pp);
        }

        return project;
    }
    
    /// <summary>
    /// Exports an AudioEffect to a ProjectEffect
    /// </summary>
    private static ProjectEffect ExportEffect(AudioEffect effect, int slotIndex)
    {
        var projectEffect = new ProjectEffect
        {
            Id = Guid.NewGuid().ToString(),
            Name = effect.Name,
            Type = effect.EffectType,
            Icon = effect.Icon,
            IsEnabled = effect.IsEnabled,
            IsExpanded = effect.IsExpanded,
            SlotIndex = slotIndex
        };
        
        // Export specific effect parameters
        switch (effect)
        {
            case EqualizerEffect eq:
                projectEffect.Equalizer = new EqualizerParameters
                {
                    LowGain = eq.LowGain,
                    MidGain = eq.MidGain,
                    HighGain = eq.HighGain,
                    Bands = eq.Bands.Select(b => new EqBandParameters
                    {
                        Number = b.Number,
                        Gain = b.Gain,
                        Frequency = b.Frequency,
                        Q = b.Q,
                        Mode = (int)b.Mode,
                        IsEnabled = b.IsEnabled
                    }).ToList()
                };
                break;
                
            case CompressorEffect comp:
                projectEffect.Compressor = new CompressorParameters
                {
                    Threshold = comp.Threshold,
                    Ratio = comp.Ratio,
                    Attack = comp.Attack,
                    Release = comp.Release,
                    MakeupGain = comp.MakeupGain,
                    Knee = comp.Knee
                };
                break;
                
            case ReverbEffect rev:
                projectEffect.Reverb = new ReverbParameters
                {
                    RoomSize = rev.RoomSize,
                    Damping = rev.Damping,
                    WetLevel = rev.WetLevel,
                    Mix = rev.Mix,
                    PreDelay = rev.PreDelay,
                    Depth = rev.Depth,
                    ReverbMode = rev.ReverbMode,
                    EarlySize = rev.EarlySize,
                    EarlyDiffusion = rev.EarlyDiffusion,
                    EarlyCross = rev.EarlyCross,
                    EarlySend = rev.EarlySend,
                    EarlyModRate = rev.EarlyModRate,
                    EarlyModDepth = rev.EarlyModDepth,
                    LateSize = rev.LateSize,
                    LateCross = rev.LateCross,
                    Decay = rev.Decay,
                    BassMult = rev.BassMult,
                    BassXover = rev.BassXover,
                    HighCut = rev.HighCut,
                    HighShelf = rev.HighShelf,
                    LowShelf = rev.LowShelf
                };
                break;
                
            case DelayEffect delay:
                projectEffect.Delay = new DelayParameters
                {
                    DelayTime = delay.DelayTime,
                    Feedback = delay.Feedback,
                    WetMix = delay.WetLevel
                };
                break;
                
            case GainEffect gain:
                projectEffect.Gain = new GainParameters
                {
                    GainDb = gain.Gain
                };
                break;
        }
        
        return projectEffect;
    }
    
    /// <summary>
    /// Imports an effect from project data
    /// </summary>
    private static AudioEffect? ImportEffect(ProjectEffect projectEffect)
    {
        AudioEffect? effect = EffectFactory.Create(projectEffect.Type);
        if (effect == null) return null;
        
        effect.IsEnabled = projectEffect.IsEnabled;
        effect.IsExpanded = projectEffect.IsExpanded;
        
        // Import specific parameters
        switch (effect)
        {
            case EqualizerEffect eq when projectEffect.Equalizer != null:
                if (projectEffect.Equalizer.Bands is { Count: > 0 } bands)
                {
                    foreach (var bp in bands)
                    {
                        var idx = bp.Number - 1;
                        if (idx >= 0 && idx < EqualizerEffect.BandCount)
                        {
                            eq.Bands[idx].Gain = bp.Gain;
                            eq.Bands[idx].Frequency = bp.Frequency;
                            eq.Bands[idx].Q = bp.Q;
                            eq.Bands[idx].Mode = (EqBandMode)bp.Mode;
                            eq.Bands[idx].IsEnabled = bp.IsEnabled;
                        }
                    }
                }
                else
                {
                    // Legacy 3-band fallback
                    eq.LowGain = projectEffect.Equalizer.LowGain;
                    eq.MidGain = projectEffect.Equalizer.MidGain;
                    eq.HighGain = projectEffect.Equalizer.HighGain;
                }
                break;
                
            case CompressorEffect comp when projectEffect.Compressor != null:
                comp.Threshold = projectEffect.Compressor.Threshold;
                comp.Ratio = projectEffect.Compressor.Ratio;
                comp.Attack = projectEffect.Compressor.Attack;
                comp.Release = projectEffect.Compressor.Release;
                comp.MakeupGain = projectEffect.Compressor.MakeupGain;
                comp.Knee = projectEffect.Compressor.Knee;
                break;
                
            case ReverbEffect rev when projectEffect.Reverb != null:
                var rp = projectEffect.Reverb;
                // Check if this is a new-style project (has non-default Mix)
                if (rp.Mix > 0 || rp.EarlySize > 0 || rp.Decay > 0.1)
                {
                    rev.Mix = rp.Mix;
                    rev.PreDelay = rp.PreDelay;
                    rev.Depth = rp.Depth;
                    rev.ReverbMode = rp.ReverbMode;
                    rev.EarlySize = rp.EarlySize;
                    rev.EarlyDiffusion = rp.EarlyDiffusion;
                    rev.EarlyCross = rp.EarlyCross;
                    rev.EarlySend = rp.EarlySend;
                    rev.EarlyModRate = rp.EarlyModRate;
                    rev.EarlyModDepth = rp.EarlyModDepth;
                    rev.LateSize = rp.LateSize;
                    rev.LateCross = rp.LateCross;
                    rev.Decay = rp.Decay;
                    rev.BassMult = rp.BassMult;
                    rev.BassXover = rp.BassXover;
                    rev.HighCut = rp.HighCut;
                    rev.HighShelf = rp.HighShelf;
                    rev.LowShelf = rp.LowShelf;
                }
                else
                {
                    // Legacy project: map old params to new
                    rev.RoomSize = rp.RoomSize;
                    rev.Damping = rp.Damping;
                    rev.WetLevel = rp.WetLevel;
                }
                break;
                
            case DelayEffect delay when projectEffect.Delay != null:
                delay.DelayTime = projectEffect.Delay.DelayTime;
                delay.Feedback = projectEffect.Delay.Feedback;
                delay.WetLevel = projectEffect.Delay.WetMix;
                break;
                
            case GainEffect gain when projectEffect.Gain != null:
                gain.Gain = projectEffect.Gain.GainDb;
                break;
        }
        
        return effect;
    }
    
    /// <summary>
    /// Adds a file reference to the project
    /// </summary>
    private static void AddFileReference(DawProject project, string filePath)
    {
        if (project.Files.Any(f => f.OriginalPath == filePath)) return;
        
        var fileRef = new ProjectFileReference
        {
            OriginalPath = filePath,
            FileName = Path.GetFileName(filePath)
        };
        
        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            fileRef.FileSizeBytes = fileInfo.Length;
            fileRef.LastModified = fileInfo.LastWriteTime;
            
            // Calculate relative path if project has a file path
            if (!string.IsNullOrEmpty(project.FilePath))
            {
                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    fileRef.RelativePath = Path.GetRelativePath(projectDir, filePath);
                }
            }
        }
        
        project.Files.Add(fileRef);
    }

    public async Task ImportProjectState(DawProject project, MainViewModel mainViewModel)
    {
        mainViewModel.Tracks.Clear();

        // Import global settings
        mainViewModel.BPM = project.Settings.BPM;
        mainViewModel.MasterVolume = project.MasterChannel.Volume;
        mainViewModel.MasterPan = project.MasterChannel.Pan;
        
        // Import master effects
        foreach (var slot in mainViewModel.MasterEffectSlots)
        {
            slot.Effect = null;
            slot.IsExpanded = false;
        }
        
        foreach (var projectEffect in project.MasterChannel.Effects)
        {
            var slotIndex = projectEffect.SlotIndex;
            if (slotIndex >= 0 && slotIndex < mainViewModel.MasterEffectSlots.Count)
            {
                var effect = ImportEffect(projectEffect);
                if (effect != null)
                {
                    var slot = mainViewModel.MasterEffectSlots[slotIndex];
                    slot.Effect = effect;
                    slot.IsExpanded = projectEffect.IsExpanded;
                }
            }
        }

        // Import tracks
        foreach (var projectTrack in project.Tracks.OrderBy(t => t.TrackNumber))
        {
            var track = new Track
            {
                TrackNumber = projectTrack.TrackNumber,
                Title = projectTrack.Title,
                Artist = projectTrack.Artist,
                FilePath = projectTrack.FilePath,
                ChannelColor = projectTrack.Color.ToMediaColor(),
                Volume = projectTrack.Volume,
                Pan = projectTrack.Pan,
                IsMuted = projectTrack.IsMuted,
                IsSolo = projectTrack.IsSolo,
                IsArmed = projectTrack.IsArmed
            };
            
            // Import track effects into slots (preserves slot positions)
            foreach (var projectEffect in projectTrack.Effects.OrderBy(e => e.SlotIndex))
            {
                var effect = ImportEffect(projectEffect);
                if (effect != null)
                {
                    var slotIndex = projectEffect.SlotIndex;
                    var slot = track.EffectSlots.FirstOrDefault(s => s.SlotNumber == slotIndex);
                    if (slot != null && !slot.HasEffect)
                    {
                        slot.Effect = effect;
                    }
                    else
                    {
                        // Fallback: find first empty slot
                        var emptySlot = track.EffectSlots.FirstOrDefault(s => !s.HasEffect);
                        if (emptySlot != null)
                            emptySlot.Effect = effect;
                    }
                }
            }

            mainViewModel.Tracks.Add(track);
            await ImportClipsForTrack(projectTrack, track, mainViewModel);
        }

        // ── Restore mixer channel routing and display order ───────────────
        if (project.MixerChannelLayout.Count > 0)
        {
            // Apply send targets
            foreach (var layout in project.MixerChannelLayout)
            {
                var ch = mainViewModel.MixerChannels
                    .FirstOrDefault(c => c.SourceTrack?.TrackNumber.ToString() == layout.SourceTrackId
                                      || (layout.SourceTrackId == null && c.SourceTrack == null && c.ChannelNumber == layout.ChannelNumber));
                if (ch == null) continue;

                ch.SendTargets.Clear();
                foreach (var target in layout.SendTargets)
                    ch.AddSend(target);
            }

            // Restore display order
            var ordered = project.MixerChannelLayout
                .OrderBy(l => l.DisplayOrder)
                .Select(l => mainViewModel.MixerChannels
                    .FirstOrDefault(c => c.SourceTrack?.TrackNumber.ToString() == l.SourceTrackId
                                      || (l.SourceTrackId == null && c.SourceTrack == null && c.ChannelNumber == l.ChannelNumber)))
                .Where(c => c != null)
                .Cast<DAW.Models.Mixer.MixerChannel>()
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                int current = mainViewModel.MixerChannels.IndexOf(ordered[i]);
                if (current != i && current >= 0)
                    mainViewModel.MixerChannels.Move(current, i);
            }
        }

        // ── Import patterns (Channel Rack + Piano Roll) ───────────────────
        if (project.Patterns.Count > 0)
        {
            var patternVm = mainViewModel.PatternVm;

            // Remove all existing patterns
            patternVm.AllPatterns.Clear();

            foreach (var pp in project.Patterns)
            {
                var model = new DAW.Models.Sequencer.PatternModel
                {
                    Name      = pp.Name,
                    StepCount = pp.StepCount,
                    Swing     = pp.Swing
                };

                foreach (var pc in pp.Channels)
                {
                    // Parse the stored color; fall back to DodgerBlue on failure
                    System.Windows.Media.Color channelColor;
                    try { channelColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(pc.ChannelColor); }
                    catch { channelColor = System.Windows.Media.Colors.DodgerBlue; }

                    var ch = new DAW.Models.Sequencer.ChannelModel(pc.Name, pp.StepCount)
                    {
                        SamplePath   = pc.SamplePath,
                        PluginIcon   = pc.PluginIcon,
                        ChannelColor = channelColor,
                        IsMuted      = pc.IsMuted,
                        MixerTrack   = pc.MixerTrack,
                        Volume       = pc.Volume
                    };

                    // Restore steps
                    for (int i = 0; i < pc.Steps.Count && i < ch.Steps.Count; i++)
                    {
                        ch.Steps[i].IsActive = pc.Steps[i].IsActive;
                        ch.Steps[i].Velocity = pc.Steps[i].Velocity;
                        ch.Steps[i].Pan      = pc.Steps[i].Pan;
                        ch.Steps[i].Pitch    = pc.Steps[i].Pitch;
                    }

                    // Restore Piano Roll notes
                    foreach (var pn in pc.PianoRollNotes)
                    {
                        ch.PianoRollNotes.Add(new DAW.Models.PianoRoll.PianoRollNote
                        {
                            Pitch     = pn.Pitch,
                            StartTick = pn.StartTick,
                            Length    = pn.Length,
                            Velocity  = pn.Velocity,
                            Pan       = pn.Pan,
                            Release   = pn.Release,
                            IsMuted   = pn.IsMuted
                        });
                    }

                    model.Channels.Add(ch);
                }

                patternVm.AllPatterns.Add(model);
            }

            // Restore the active pattern
            var active = string.IsNullOrEmpty(project.ActivePatternName)
                ? patternVm.AllPatterns.FirstOrDefault()
                : patternVm.AllPatterns.FirstOrDefault(p => p.Name == project.ActivePatternName)
                  ?? patternVm.AllPatterns.FirstOrDefault();

            patternVm.ActivePattern = active;

            // Preload audio for all restored channels
            foreach (var ch in patternVm.Channels)
            {
                ch.AudioEngine = mainViewModel.MixEngine;
                _ = ch.PreloadSampleAsync();
            }
        }
    }

    private async Task ImportClipsForTrack(ProjectTrack projectTrack, Track track, MainViewModel mainViewModel)
    {
        var arrangementTrack = mainViewModel.ArrangementVm.Tracks
            .FirstOrDefault(t => t.Model == track);

        if (arrangementTrack == null) return;

        foreach (var projectClip in projectTrack.Clips)
        {
            var clip = new ArrangementClip
            {
                DisplayName = projectClip.DisplayName,
                StartBeat = projectClip.StartBeat,
                LengthInBeats = projectClip.LengthInBeats,
                SourceFilePath = projectClip.SourceFilePath,
                Color = projectClip.Color.ToMediaColor(),
                WaveformData = projectClip.WaveformData
            };

            var clipVm = new ArrangementClipViewModel(clip, mainViewModel.ArrangementVm);
            arrangementTrack.Clips.Add(clipVm);
        }
    }

    #endregion

    private void SetUnsavedChanges(bool hasChanges)
    {
        if (_hasUnsavedChanges != hasChanges)
        {
            _hasUnsavedChanges = hasChanges;
            UnsavedChangesChanged?.Invoke(this, hasChanges);
        }
    }
}

#region Event Args

public sealed class ProjectLoadedEventArgs : EventArgs
{
    public ProjectLoadedEventArgs(DawProject project, string? filePath)
    {
        Project = project;
        FilePath = filePath;
    }

    public DawProject Project { get; }
    public string? FilePath { get; }
}

public sealed class ProjectSavedEventArgs : EventArgs
{
    public ProjectSavedEventArgs(DawProject project, string filePath)
    {
        Project = project;
        FilePath = filePath;
    }

    public DawProject Project { get; }
    public string FilePath { get; }
}

#endregion