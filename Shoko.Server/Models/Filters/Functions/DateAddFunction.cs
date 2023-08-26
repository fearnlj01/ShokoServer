using System;
using Shoko.Server.Models.Filters.Interfaces;

namespace Shoko.Server.Models.Filters.Functions;

public class DateAddFunction : FilterExpression<DateTime?>
{
    public FilterExpression<DateTime?> Selector { get; set; }
    public TimeSpan Parameter { get; set; }

    public override bool TimeDependent => Selector.TimeDependent;
    public override bool UserDependent => Selector.UserDependent;

    public override DateTime? Evaluate(IFilterable f) => Selector.Evaluate(f) + Parameter;
}
