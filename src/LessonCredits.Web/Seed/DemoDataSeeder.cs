using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using LessonCredits.Core.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Web.Seed;

/// <summary>
/// 啟動時套用 migration；資料庫為空時建立範例資料。
/// 範例資料不是直接塞進資料表，而是用可調整的時鐘「重演」過去 10 週的購買、請假、點名、補課與退費，
/// 全部經過正式的服務層，因此一定符合業務規則，日期也會相對於啟動當天。
/// </summary>
public static class DemoDataSeeder
{
    public const string OwnerPassword = "owner123";
    public const string TeacherPassword = "teacher123";

    private const int HistoryWeeks = 10;

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var dbFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            if (!await db.Users.AnyAsync())
            {
                var realNow = services.GetRequiredService<TaipeiClock>().Now;
                await new Replay(dbFactory, services.GetRequiredService<IPasswordHasher<User>>(), realNow).RunAsync();
            }
        }

        // 每次啟動都補齊未來 8 週的場次
        await services.CreateScope().ServiceProvider.GetRequiredService<ScheduleService>().GenerateSessionsAsync();
    }

    private sealed class ReplayTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class Replay
    {
        private readonly IDbContextFactory<AppDbContext> _db;
        private readonly IPasswordHasher<User> _hasher;
        private readonly DateTime _realNow;
        private readonly DateOnly _today;
        private readonly ReplayTimeProvider _time = new();
        private readonly Actor _owner = new(1, Role.Owner, null);
        private readonly CreditService _credits;
        private readonly ScheduleService _schedule;
        private readonly AttendanceService _attendance;
        private readonly MakeupService _makeup;
        private readonly RefundService _refunds;
        private readonly AdminService _admin;

        public Replay(IDbContextFactory<AppDbContext> db, IPasswordHasher<User> hasher, DateTime realNow)
        {
            _db = db;
            _hasher = hasher;
            _realNow = realNow;
            _today = DateOnly.FromDateTime(realNow);
            var clock = new TaipeiClock(_time);
            _credits = new CreditService(db, clock);
            _schedule = new ScheduleService(db, clock);
            _attendance = new AttendanceService(db, clock);
            _makeup = new MakeupService(db, clock);
            _refunds = new RefundService(db, clock);
            _admin = new AdminService(db, clock);
        }

        public async Task RunAsync()
        {
            var teachers = await SeedPeopleAsync();
            var students = await SeedStudentsAsync();
            var plans = await SeedPurchasesAsync(students);
            await SeedCoursesAsync(teachers, students);

            var historyStart = MondayOf(_today).AddDays(-7 * HistoryWeeks);
            SetNow(historyStart.ToDateTime(new TimeOnly(8, 0)));
            await _schedule.GenerateSessionsAsync();
            SetNow(historyStart.AddDays(7 * 7).ToDateTime(new TimeOnly(8, 0)));
            await _schedule.GenerateSessionsAsync();

            await SeedClosuresAsync(historyStart);
            await ReplayHistoryAsync(historyStart);
            await SeedUpcomingAsync(students);

            // 退費示範：志明有兩種單價與一批過期點數，於今天確認退費
            SetNow(_realNow.AddMinutes(-30));
            await _refunds.ConfirmAsync(students["吳志明"], _owner);

            // 示範今天剛購買
            SetNow(_realNow.AddMinutes(-10));
            await _credits.PurchaseAsync(students["楊詠晴"], plans.Ten, _owner);
        }

        private async Task<Dictionary<string, int>> SeedPeopleAsync()
        {
            await using var db = await _db.CreateDbContextAsync();
            var owner = new User { UserName = "owner", DisplayName = "林店長", Role = Role.Owner };
            owner.PasswordHash = _hasher.HashPassword(owner, OwnerPassword);
            db.Users.Add(owner);

            var teachers = new (string UserName, string Name)[]
            {
                ("chen", "陳美玲"), ("lin", "林志豪"), ("wang", "王雅婷"), ("huang", "黃建宏"),
            };
            foreach (var (userName, name) in teachers)
            {
                var user = new User { UserName = userName, DisplayName = name + "老師", Role = Role.Teacher, Teacher = new Teacher { Name = name } };
                user.PasswordHash = _hasher.HashPassword(user, TeacherPassword);
                db.Users.Add(user);
            }

            await db.SaveChangesAsync();
            return await db.Teachers.ToDictionaryAsync(t => t.Name, t => t.Id);
        }

        private async Task<Dictionary<string, int>> SeedStudentsAsync()
        {
            // 家長 → 學生（兄弟姊妹共用一位家長）
            var families = new (string Parent, string Phone, string[] Children)[]
            {
                ("張淑芬", "0912-345-678", ["張家豪", "張家瑜"]),
                ("李建國", "0922-111-222", ["李宜庭"]),
                ("王美華", "0933-222-333", ["王柏翰", "王柏宇"]),
                ("陳志偉", "0955-333-444", ["陳思妤"]),
                ("林雅惠", "0966-444-555", ["林冠廷", "林冠宇"]),
                ("黃俊傑", "0977-555-666", ["黃筱涵"]),
                ("吳佩珊", "0988-666-777", ["吳志明"]),
                ("劉文雄", "0910-777-888", ["劉子晴", "劉子軒"]),
                ("蔡淑貞", "0921-888-999", ["蔡宇恩", "蔡宇彤"]),
                ("楊宗翰", "0932-999-000", ["楊詠晴"]),
                ("許秀英", "0952-123-456", ["許安妮", "許安琪"]),
                ("鄭明哲", "0963-234-567", ["鄭宇翔"]),
                ("謝佳玲", "0975-345-678", ["謝承恩"]),
                ("郭建志", "0986-456-789", ["郭品妍"]),
                ("洪麗娟", "0911-567-890", ["洪子涵", "洪子睿"]),
                ("曾國華", "0928-678-901", ["曾語彤", "曾語涵"]),
                ("邱淑惠", "0937-789-012", ["邱柏諺"]),
                ("廖俊宏", "0958-890-123", ["廖芷晴", "廖芷瑄"]),
                ("賴美玲", "0968-901-234", ["賴品睿"]),
                ("周志豪", "0979-012-345", ["周昱廷", "周昱安"]),
            };

            var ids = new Dictionary<string, int>();
            foreach (var (parentName, phone, children) in families)
            {
                var parent = await _admin.AddParentAsync(parentName, phone, _owner);
                foreach (var child in children)
                {
                    ids[child] = (await _admin.AddStudentAsync(parent.Id, child, _owner)).Id;
                }
            }

            return ids;
        }

        private sealed record Plans(int Trial, int Ten, int Twenty);

        private async Task<Plans> SeedPurchasesAsync(Dictionary<string, int> students)
        {
            await _admin.SavePlanAsync(null, "單堂體驗", 1, 900, true, _owner);
            await _admin.SavePlanAsync(null, "10 堂方案", 10, 8000, true, _owner);
            await _admin.SavePlanAsync(null, "20 堂方案", 20, 14000, true, _owner); // 舊價格，稍後調漲
            var all = await _admin.ListPlansAsync();
            var plans = new Plans(all.Single(p => p.Lessons == 1).Id, all.Single(p => p.Lessons == 10).Id, all.Single(p => p.Lessons == 20).Id);

            // 舊價時期購買（單價 700）
            var oldPrice = new[] { "張家瑜", "李宜庭", "吳志明", "許安妮", "賴品睿" };
            foreach (var name in oldPrice)
            {
                await BuyAsync(students[name], plans.Twenty, -150);
            }

            await _admin.SavePlanAsync(plans.Twenty, "20 堂方案", 20, 15000, true, _owner);

            // 早年購買、從未使用、已過期的單堂體驗
            foreach (var name in new[] { "王柏宇", "吳志明", "許安妮", "邱柏諺", "黃筱涵" })
            {
                await BuyAsync(students[name], plans.Trial, -260);
            }

            // 即將到期：20 天後到期
            foreach (var name in new[] { "陳思妤", "劉子晴", "洪子睿" })
            {
                SetNow(_today.AddDays(20).AddMonths(-6).ToDateTime(new TimeOnly(11, 0)));
                await _credits.PurchaseAsync(students[name], plans.Twenty, _owner);
            }

            // 一般 20 堂
            foreach (var name in new[] { "張家豪", "王柏翰", "林冠廷", "劉子軒", "許安琪", "謝承恩", "洪子涵", "曾語彤", "廖芷晴", "周昱廷", "王柏宇", "邱柏諺", "黃筱涵" })
            {
                await BuyAsync(students[name], plans.Twenty, -75);
            }

            // 點數將用完：10 堂
            foreach (var name in new[] { "林冠宇", "蔡宇恩", "鄭宇翔" })
            {
                await BuyAsync(students[name], plans.Ten, -80);
            }

            // 第二批（不同單價）
            foreach (var name in new[] { "張家瑜", "吳志明", "許安妮", "賴品睿" })
            {
                await BuyAsync(students[name], plans.Ten, -20);
            }

            // 新生：上週購買，尚未排課
            foreach (var name in new[] { "蔡宇彤", "曾語涵", "廖芷瑄", "周昱安" })
            {
                await BuyAsync(students[name], plans.Ten, -5);
            }

            // 只買了單堂體驗（郭品妍用完後無法再點名出席）
            await BuyAsync(students["楊詠晴"], plans.Trial, -10);
            await BuyAsync(students["郭品妍"], plans.Trial, -60);
            return plans;
        }

        private async Task SeedCoursesAsync(Dictionary<string, int> teachers, Dictionary<string, int> students)
        {
            var courses = new (NewCourse Course, string[] Students)[]
            {
                (new("鋼琴小班 A", Subject.Piano, CourseType.Group, teachers["陳美玲"], DayOfWeek.Wednesday, new(16, 0), 60), ["張家豪", "張家瑜", "林冠宇", "郭品妍"]),
                (new("鋼琴小班 B", Subject.Piano, CourseType.Group, teachers["林志豪"], DayOfWeek.Saturday, new(10, 0), 60), ["王柏翰", "陳思妤", "王柏宇"]),
                (new("美術小班 A", Subject.Art, CourseType.Group, teachers["王雅婷"], DayOfWeek.Tuesday, new(16, 30), 90), ["李宜庭", "吳志明", "劉子晴"]),
                (new("美術小班 B", Subject.Art, CourseType.Group, teachers["黃建宏"], DayOfWeek.Saturday, new(14, 0), 90), ["劉子軒", "許安妮", "蔡宇恩"]),
                (new("美術小班 C", Subject.Art, CourseType.Group, teachers["黃建宏"], DayOfWeek.Sunday, new(10, 0), 90), ["許安琪", "洪子睿", "楊詠晴"]),
                (new("鋼琴個別課（週一 16:00）", Subject.Piano, CourseType.OneOnOne, teachers["陳美玲"], DayOfWeek.Monday, new(16, 0), 50), ["林冠廷"]),
                (new("鋼琴個別課（週一 17:00）", Subject.Piano, CourseType.OneOnOne, teachers["陳美玲"], DayOfWeek.Monday, new(17, 0), 50), ["謝承恩"]),
                (new("鋼琴個別課（週四 16:00）", Subject.Piano, CourseType.OneOnOne, teachers["林志豪"], DayOfWeek.Thursday, new(16, 0), 50), ["洪子涵"]),
                (new("鋼琴個別課（週四 17:00）", Subject.Piano, CourseType.OneOnOne, teachers["林志豪"], DayOfWeek.Thursday, new(17, 0), 50), ["鄭宇翔"]),
                (new("美術個別課（週五 16:00）", Subject.Art, CourseType.OneOnOne, teachers["王雅婷"], DayOfWeek.Friday, new(16, 0), 60), ["賴品睿"]),
            };

            foreach (var (input, names) in courses)
            {
                var course = await _admin.AddCourseAsync(input, _owner);
                foreach (var name in names)
                {
                    await _admin.EnrollAsync(course.Id, students[name], _owner);
                }
            }
        }

        private async Task SeedClosuresAsync(DateOnly historyStart)
        {
            // 過去一個週三颱風停課（鋼琴小班 A 該日取消），以及未來一個週六國定假日
            SetNow(historyStart.ToDateTime(new TimeOnly(8, 0)));
            var pastWednesday = NextOrSame(_today.AddDays(-25), DayOfWeek.Wednesday);
            await _schedule.AddClosureDayAsync(pastWednesday, "颱風停課", _owner);
            var futureSaturday = NextOrSame(_today.AddDays(14), DayOfWeek.Saturday);
            await _schedule.AddClosureDayAsync(futureSaturday, "國定假日", _owner);
        }

        /// <summary>依週重演：先登記請假，再點名，週末替本週產生的補課資格預約下週的小班。</summary>
        private async Task ReplayHistoryAsync(DateOnly historyStart)
        {
            for (var week = historyStart; week <= MondayOf(_today); week = week.AddDays(7))
            {
                var sessions = (await SessionsAsync(week, week.AddDays(7)))
                    .Where(s => s.Status == SessionStatus.Scheduled)
                    .ToList();
                foreach (var session in sessions)
                {
                    await RequestLeavesAsync(session);
                }

                foreach (var session in sessions.Where(s => s.StartAt.AddHours(1) <= _realNow))
                {
                    await RecordAsync(session);
                }

                var weekend = week.AddDays(6).ToDateTime(new TimeOnly(21, 0));
                if (weekend < _realNow)
                {
                    SetNow(weekend);
                    await BookMakeupsAsync(everyOther: true);
                }
            }
        }

        private async Task RequestLeavesAsync(SessionSummary session)
        {
            var roster = await RosterAsync(session.SessionId);
            foreach (var row in roster.Rows.Where(r => !r.IsMakeup))
            {
                var pick = Pick(row.StudentId, session.SessionId);
                var requestAt = pick switch
                {
                    0 => session.StartAt.AddDays(-2),   // 準時請假
                    1 => session.StartAt.AddHours(-3),  // 太晚請假 → 缺席
                    _ => (DateTime?)null,
                };
                if (requestAt is { } at && at < _realNow)
                {
                    SetNow(at);
                    await _attendance.RequestLeaveAsync(session.SessionId, row.StudentId, _owner);
                }
            }
        }

        private async Task RecordAsync(SessionSummary session)
        {
            var roster = await RosterAsync(session.SessionId);
            SetNow(session.StartAt.AddMinutes(50));
            foreach (var row in roster.Rows)
            {
                var pick = Pick(row.StudentId, session.SessionId);
                var status = row switch
                {
                    { IsMakeup: true } => (row.StudentId + session.SessionId) % 3 == 0 ? AttendanceStatus.Absent : AttendanceStatus.Present,
                    { Leave.IsOnTime: true } => AttendanceStatus.Leave,
                    { Leave: not null } => AttendanceStatus.Absent,
                    _ => pick == 2 ? AttendanceStatus.Absent : AttendanceStatus.Present,
                };

                try
                {
                    await _attendance.RecordAsync(session.SessionId, row.StudentId, status, _owner);
                }
                catch (BusinessRuleException)
                {
                    // 點數用完的學生無法點名，保留未點名狀態作為示範
                }
            }
        }

        private async Task BookMakeupsAsync(bool everyOther)
        {
            var credits = await _makeup.ListCreditsAsync();
            foreach (var credit in credits.Where(c => c.Status == MakeupStatus.Available && !c.IsExpired))
            {
                if (everyOther && credit.Id % 2 == 1)
                {
                    continue;
                }

                var options = await _makeup.FindBookableSessionsAsync(credit.Id);
                if (options.Count > 0)
                {
                    await _makeup.BookAsync(credit.Id, options[0].SessionId, _owner);
                }
            }
        }

        private async Task SeedUpcomingAsync(Dictionary<string, int> students)
        {
            SetNow(_realNow.AddMinutes(-60));
            await BookMakeupsAsync(everyOther: false);

            // 下週的準時請假示範
            var upcoming = await SessionsAsync(_today.AddDays(3), _today.AddDays(10));
            foreach (var session in upcoming.Where(s => s.Status == SessionStatus.Scheduled).Take(3))
            {
                var roster = await RosterAsync(session.SessionId);
                var row = roster.Rows.FirstOrDefault(r => !r.IsMakeup && r.Leave is null);
                if (row is not null)
                {
                    await _attendance.RequestLeaveAsync(session.SessionId, row.StudentId, _owner);
                }
            }
        }

        private async Task BuyAsync(int studentId, int planId, int daysAgo)
        {
            SetNow(_today.AddDays(daysAgo).ToDateTime(new TimeOnly(11, 0)));
            await _credits.PurchaseAsync(studentId, planId, _owner);
        }

        private Task<List<SessionSummary>> SessionsAsync(DateOnly from, DateOnly toExclusive) =>
            _attendance.ListSessionsAsync(from, toExclusive.AddDays(-1), _owner);

        private Task<SessionRoster> RosterAsync(int sessionId) => _attendance.GetRosterAsync(sessionId, _owner);

        private void SetNow(DateTime taipeiLocal) =>
            _time.UtcNow = new DateTimeOffset(taipeiLocal, TimeSpan.FromHours(8));

        /// <summary>決定性的「隨機」：0 = 準時請假、1 = 太晚請假、2 = 缺席、其他 = 出席。</summary>
        private static int Pick(int studentId, int sessionId) => (studentId * 7 + sessionId * 13) % 11;

        private static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

        private static DateOnly NextOrSame(DateOnly date, DayOfWeek day) => date.AddDays(((int)day - (int)date.DayOfWeek + 7) % 7);
    }
}
