namespace Cya2.Core.Entities;

public abstract class BaseEntity
{
    public int Id { get; protected set; }
    public DateTime DateCreated { get; protected set; } = DateTime.UtcNow;
    public DateTime? DateModified { get; protected set; }

    protected void SetModified()
    {
        DateModified = DateTime.UtcNow;
    }
}