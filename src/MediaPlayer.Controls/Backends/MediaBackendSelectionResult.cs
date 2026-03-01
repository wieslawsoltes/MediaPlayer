using System.Collections.Generic;

namespace MediaPlayer.Controls.Backends;

internal readonly record struct MediaBackendSelectionResult(
    IReadOnlyList<MediaBackendCandidate> Candidates,
    string ModeSelectionWarning);
