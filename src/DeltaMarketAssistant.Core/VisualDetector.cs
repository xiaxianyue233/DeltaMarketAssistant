using System.Drawing;
using DeltaMarketAssistant.Core.Models;

namespace DeltaMarketAssistant.Core;

public sealed class VisualDetector
{
    private readonly AutomationSettings _settings;
    private readonly CalibrationProfile _calibration;

    public VisualDetector(AutomationSettings settings, CalibrationProfile calibration)
    {
        _settings = settings;
        _calibration = calibration;
    }

    public bool IsPrimaryActive(Bitmap primaryButton)
    {
        var stats = VisualAnalyzer.Analyze(primaryButton);
        var difference = VisualAnalyzer.FingerprintDifference(
            primaryButton,
            _calibration.PrimaryInactiveFingerprint);
        return !stats.IsBlank &&
               stats.GreenRatio >= _settings.PrimaryGreenRatioMin &&
               difference >= _settings.PrimaryDifferenceMin;
    }

    public bool IsDialogHeaderPresent(Bitmap dialogHeader)
    {
        var stats = VisualAnalyzer.Analyze(dialogHeader);
        var difference = VisualAnalyzer.FingerprintDifference(
            dialogHeader,
            _calibration.DialogHeaderFingerprint);
        return stats.HasDetail && difference <= _settings.DialogHeaderDifferenceMax;
    }

    public bool IsConfirmReady(Bitmap confirmButton, Bitmap dialogHeader)
    {
        return IsConfirmButtonActive(confirmButton) &&
               IsDialogHeaderPresent(dialogHeader);
    }

    public bool IsOriginalDialogHeaderPresent(Bitmap header, string lockedFingerprint) =>
        VisualAnalyzer.Analyze(header).HasDetail &&
        VisualAnalyzer.FingerprintDifference(header, lockedFingerprint) <= 0.08;

    public bool IsConfirmButtonActive(Bitmap confirmButton)
    {
        var stats = VisualAnalyzer.Analyze(confirmButton);
        return !stats.IsBlank && stats.GreenRatio >= _settings.ConfirmGreenRatioMin;
    }

    public bool IsLockedConfirmSurfacePresent(Bitmap confirmButton)
    {
        var stats = VisualAnalyzer.Analyze(confirmButton);
        return !stats.IsBlank && stats.HasDetail;
    }

    public bool IsMainScreenWithRefresh(Bitmap mainAnchor, Bitmap refreshButton)
    {
        return IsMainScreen(mainAnchor) &&
               VisualAnalyzer.Analyze(refreshButton).HasDetail &&
               VisualAnalyzer.FingerprintDifference(
                   refreshButton, _calibration.RefreshFingerprint) <= 0.06;
    }

    public bool IsMainScreen(Bitmap mainAnchor)
    {
        var stats = VisualAnalyzer.Analyze(mainAnchor);
        var difference = VisualAnalyzer.FingerprintDifference(
            mainAnchor,
            _calibration.MainAnchorFingerprint);
        return stats.HasDetail && difference <= _settings.MainSignatureDifferenceMax;
    }
}
