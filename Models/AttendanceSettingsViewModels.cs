using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public sealed class AttendanceSettingsViewModel
{
    public AttendanceScheduleSettingsForm Schedule { get; set; } = AttendanceScheduleSettingsForm.Default();
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
}

public sealed class AttendanceScheduleSettingsForm : IValidatableObject
{
    [Required(ErrorMessage = "Vui lòng nhập giờ bắt đầu buổi sáng.")]
    [DataType(DataType.Time)]
    public TimeSpan MorningStart { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giờ kết thúc buổi sáng.")]
    [DataType(DataType.Time)]
    public TimeSpan MorningEnd { get; set; }

    [Range(0, 240, ErrorMessage = "Số phút cho phép trễ buổi sáng phải từ 0 đến 240.")]
    public int MorningLateGraceMinutes { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giờ bắt đầu buổi chiều.")]
    [DataType(DataType.Time)]
    public TimeSpan AfternoonStart { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giờ kết thúc buổi chiều.")]
    [DataType(DataType.Time)]
    public TimeSpan AfternoonEnd { get; set; }

    [Range(0, 240, ErrorMessage = "Số phút cho phép trễ buổi chiều phải từ 0 đến 240.")]
    public int AfternoonLateGraceMinutes { get; set; }

    public static AttendanceScheduleSettingsForm Default() => new()
    {
        MorningStart = new TimeSpan(7, 30, 0),
        MorningEnd = new TimeSpan(11, 30, 0),
        MorningLateGraceMinutes = 10,
        AfternoonStart = new TimeSpan(13, 0, 0),
        AfternoonEnd = new TimeSpan(17, 0, 0),
        AfternoonLateGraceMinutes = 10
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MorningStart >= MorningEnd)
        {
            yield return new ValidationResult(
                "Giờ bắt đầu buổi sáng phải nhỏ hơn giờ kết thúc buổi sáng.",
                [nameof(MorningStart), nameof(MorningEnd)]);
        }

        if (AfternoonStart >= AfternoonEnd)
        {
            yield return new ValidationResult(
                "Giờ bắt đầu buổi chiều phải nhỏ hơn giờ kết thúc buổi chiều.",
                [nameof(AfternoonStart), nameof(AfternoonEnd)]);
        }

        if (MorningEnd > AfternoonStart)
        {
            yield return new ValidationResult(
                "Giờ kết thúc buổi sáng không được lớn hơn giờ bắt đầu buổi chiều.",
                [nameof(MorningEnd), nameof(AfternoonStart)]);
        }
    }
}
