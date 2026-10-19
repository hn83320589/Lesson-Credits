using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;

namespace LessonCredits.Tests.Services;

public class AdminServiceTests : IDisposable
{
    private readonly TestEnv _env = new();

    [Fact]
    public async Task Changing_plan_price_does_not_affect_purchased_batches()
    {
        var student = _env.AddStudent();
        var plan = _env.AddPlan(20, 14000);
        await _env.Credits.PurchaseAsync(student, plan, _env.Owner);

        await _env.Admin.SavePlanAsync(plan, "20 堂方案", 20, 15000, true, _env.Owner);

        var detail = await _env.Admin.GetStudentDetailAsync(student, _env.Owner);
        Assert.Equal(14000, detail.Batches.Single().Amount);
        Assert.Equal(700m, detail.Batches.Single().UnitPrice);
    }

    [Fact]
    public async Task Cannot_enroll_more_than_capacity()
    {
        var teacher = _env.AddTeacher();
        var first = _env.AddStudent("甲");
        var course = _env.AddCourse(teacher, CourseType.OneOnOne, Subject.Piano, first);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _env.Admin.EnrollAsync(course, _env.AddStudent("乙"), _env.Owner));

        Assert.Contains("已滿", ex.Message);
    }

    [Fact]
    public async Task Teacher_cannot_read_student_details()
    {
        var student = _env.AddStudent();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _env.Admin.GetStudentDetailAsync(student, TestEnv.TeacherActor(_env.AddTeacher())));
    }

    [Fact]
    public async Task Teacher_only_sees_own_courses_and_sessions()
    {
        var mine = _env.AddTeacher("我");
        var other = _env.AddTeacher("別人");
        var myCourse = _env.AddCourse(mine, CourseType.Group);
        var otherCourse = _env.AddCourse(other, CourseType.Group);
        await _env.Schedule.GenerateSessionsAsync();
        var actor = TestEnv.TeacherActor(mine);

        var courses = await _env.Admin.ListCoursesAsync(actor);
        var sessions = await _env.Attendance.ListSessionsAsync(_env.Clock.Today, _env.Clock.Today.AddDays(56), actor);

        Assert.Equal([myCourse], courses.Select(c => c.Id).ToArray());
        Assert.Equal(8, sessions.Count);
        Assert.NotEqual(myCourse, otherCourse);
        var otherSession = await _env.Attendance.ListSessionsAsync(_env.Clock.Today, _env.Clock.Today.AddDays(56), _env.Owner);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _env.Attendance.GetRosterAsync(otherSession.First(s => !sessions.Any(m => m.SessionId == s.SessionId)).SessionId, actor));
    }

    public void Dispose() => _env.Dispose();
}
