using System;

namespace Shoko.Server.Filters.Selectors;

public class AddedDateSelector : FilterExpression<DateTime?>
{
    public override bool TimeDependent => false;
    public override bool UserDependent => false;

    public override DateTime? Evaluate(Filterable f)
    {
        return f.AddedDate;
    }

    protected bool Equals(AddedDateSelector other)
    {
        return base.Equals(other);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != this.GetType())
        {
            return false;
        }

        return Equals((AddedDateSelector)obj);
    }

    public override int GetHashCode()
    {
        return GetType().FullName!.GetHashCode();
    }

    public static bool operator ==(AddedDateSelector left, AddedDateSelector right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(AddedDateSelector left, AddedDateSelector right)
    {
        return !Equals(left, right);
    }
}
