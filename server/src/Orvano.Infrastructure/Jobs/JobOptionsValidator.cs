using Microsoft.Extensions.Options;

namespace Orvano.Infrastructure.Jobs;

[OptionsValidator]
internal sealed partial class JobOptionsValidator : IValidateOptions<JobOptions>;
