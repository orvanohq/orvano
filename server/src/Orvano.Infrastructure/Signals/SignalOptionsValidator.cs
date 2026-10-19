using Microsoft.Extensions.Options;

namespace Orvano.Infrastructure.Signals;

[OptionsValidator]
internal sealed partial class SignalOptionsValidator : IValidateOptions<SignalOptions>;
