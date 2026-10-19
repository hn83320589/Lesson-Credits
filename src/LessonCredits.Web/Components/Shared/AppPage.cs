using LessonCredits.Core.Services;
using LessonCredits.Web.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace LessonCredits.Web.Components.Shared;

/// <summary>
/// 頁面共用基底：取得目前操作者，並把業務規則錯誤轉成畫面訊息。
/// 只攔截預期內的業務例外；其他例外照常往上拋，不靜默吞掉。
/// </summary>
public abstract class AppPage : ComponentBase
{
    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = default!;

    protected Actor Actor { get; private set; } = default!;
    protected string? Error { get; set; }
    protected string? Warning { get; set; }
    protected string? Message { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Actor = (await AuthState).User.ToActor();
        await RunAsync(LoadAsync);
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;

    protected async Task<bool> RunAsync(Func<Task> action, string? success = null)
    {
        Error = null;
        Warning = null;
        Message = null;
        try
        {
            await action();
            Message = success;
            return true;
        }
        catch (Exception ex) when (ex is BusinessRuleException or ForbiddenException or NotFoundException)
        {
            Error = ex.Message;
            return false;
        }
    }
}
