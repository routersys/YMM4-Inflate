using System.ComponentModel.DataAnnotations;

namespace Inflate;

public enum InflateQuality
{
    [Display(Name = nameof(Texts.QualityBalanced), Description = nameof(Texts.QualityBalancedDescription), ResourceType = typeof(Texts))]
    Balanced = 0,

    [Display(Name = nameof(Texts.QualityHigh), Description = nameof(Texts.QualityHighDescription), ResourceType = typeof(Texts))]
    High = 1,

    [Display(Name = nameof(Texts.QualityUltra), Description = nameof(Texts.QualityUltraDescription), ResourceType = typeof(Texts))]
    Ultra = 2,
}
