using LessonCredits.Core.Domain;

namespace LessonCredits.Core.Services;

/// <summary>執行操作的使用者。服務層一律以參數傳入，授權檢查不依賴 UI。</summary>
public record Actor(int UserId, Role Role, int? TeacherId)
{
    public bool IsOwner => Role == Role.Owner;

    public void EnsureOwner()
    {
        if (!IsOwner)
        {
            throw new ForbiddenException("此操作僅限店長。");
        }
    }

    public bool CanAccessCourse(int courseTeacherId) => IsOwner || TeacherId == courseTeacherId;

    public void EnsureCanAccessCourse(int courseTeacherId)
    {
        if (!CanAccessCourse(courseTeacherId))
        {
            throw new ForbiddenException("您沒有權限存取其他老師的課程。");
        }
    }
}

/// <summary>違反業務規則；訊息會直接顯示給使用者。</summary>
public class BusinessRuleException(string message) : Exception(message);

public class ForbiddenException(string message) : Exception(message);

public class NotFoundException(string message) : Exception(message);
