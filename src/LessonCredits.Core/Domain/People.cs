namespace LessonCredits.Core.Domain;

public class User
{
    public int Id { get; set; }
    public required string UserName { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
}

public class Teacher
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Course> Courses { get; set; } = [];
}

public class Parent
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Phone { get; set; } = "";
    public List<Student> Students { get; set; } = [];
}

public class Student
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public Parent? Parent { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CreditBatch> Batches { get; set; } = [];
    public List<Enrollment> Enrollments { get; set; } = [];
}
