namespace Cya2.Core.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? DateModified { get; protected set; }

    protected void SetModified()
    {
        DateModified = DateTime.UtcNow;
    }
}