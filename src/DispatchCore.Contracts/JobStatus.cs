namespace DispatchCore.Contracts;

public enum JobStatus
{
    Pending,
    Scheduled,
    Running,
    Succeeded,
    Failed,
    DeadLetter
}
