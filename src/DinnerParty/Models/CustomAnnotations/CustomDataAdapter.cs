using System;
using System.Collections.Generic;
using System.ComponentModel;
using Nancy.Validation.DataAnnotations;
using System.ComponentModel.DataAnnotations;
using Nancy;
using Nancy.Validation;

namespace DinnerParty.Models.CustomAnnotations
{
    public class CustomDataAdapter : IDataAnnotationsValidatorAdapter
    {
        public bool CanHandle(ValidationAttribute attribute)
        {
            return attribute is MatchAttribute;
        }

        public IEnumerable<ModelValidationError> Validate(object instance, ValidationAttribute attribute, PropertyDescriptor descriptor, NancyContext context)
        {
            var validationContext = new ValidationContext(instance, null, null)
                          {
                              MemberName = ((MatchAttribute)attribute).SourceProperty
                          };

            var result = attribute.GetValidationResult(instance, validationContext);

            if(result != null)
            {
                yield return new ModelValidationError(result.MemberNames, attribute.ErrorMessage);
            }

            yield break;
        }

        public IEnumerable<ModelValidationRule> GetRules(ValidationAttribute attribute, PropertyDescriptor descriptor)
        {
            yield return new ModelValidationRule("custom", attribute.FormatErrorMessage,
                new[] { ((MatchAttribute)attribute).SourceProperty });
        }
    }
}