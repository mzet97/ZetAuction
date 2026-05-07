using FluentValidation;
using FluentValidation.Results;

namespace ZetAuction.Shared.Domain;

public abstract class Validatable<T> where T : Validatable<T>
{
    private IValidator<T>? _validator;

    protected void SetValidator(IValidator<T> validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public bool IsValid()
    {
        return Validate().IsValid;
    }

    public ValidationResult Validate()
    {
        if (_validator is null)
        {
            return new ValidationResult();
        }

        return _validator.Validate((T)this);
    }
}
