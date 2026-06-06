namespace MV.DomainLayer.Exceptions
{
    public class StudentNotFoundException : NotFoundException
    {
        public StudentNotFoundException(string studentId)
            : base($"Student with ID '{studentId}' was not found.") { }

        public StudentNotFoundException() : base("Student not found.") { }
    }

    public class MaxStudentsReachedException : BadRequestException
    {
        public MaxStudentsReachedException()
            : base("Maximum limit of 5 students per parent has been reached.") { }
    }

    public class NotStudentOwnerException : BadRequestException
    {
        public NotStudentOwnerException(string studentId)
            : base($"Student with ID '{studentId}' does not belong to this parent.") { }

        public NotStudentOwnerException() : base("Student does not belong to this parent.") { }
    }

    public class StudentHasActiveBookingException : BadRequestException
    {
        public StudentHasActiveBookingException()
            : base("Cannot delete student: student has active booking(s).") { }
    }

    public class InvalidBirthdateException : BadRequestException
    {
        public InvalidBirthdateException()
            : base("Birthdate must be today or in past.") { }
    }

    public class LinkCodeNotFoundException : NotFoundException
    {
        public LinkCodeNotFoundException()
            : base("Invalid link code.") { }
    }

    public class LinkCodeExpiredException : BadRequestException
    {
        public LinkCodeExpiredException()
            : base("Link code has expired.") { }
    }

    public class StudentAlreadyLinkedException : BadRequestException
    {
        public StudentAlreadyLinkedException()
            : base("This student is already linked to another account.") { }
    }

    public class ParentCodeNotFoundException : NotFoundException
    {
        public ParentCodeNotFoundException()
            : base("Invalid parent code.") { }
    }

    public class ParentCodeExpiredException : BadRequestException
    {
        public ParentCodeExpiredException()
            : base("Parent code has expired.") { }
    }

    public class StudentAlreadyHasParentException : BadRequestException
    {
        public StudentAlreadyHasParentException()
            : base("This student is already linked to a parent.") { }
    }
}
