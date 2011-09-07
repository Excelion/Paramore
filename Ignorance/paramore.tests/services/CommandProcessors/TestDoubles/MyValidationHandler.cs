using Paramore.Services.CommandHandlers;
using Paramore.Services.Common;

namespace Paramore.Tests.services.CommandProcessors.TestDoubles
{
    internal class MyValidationHandler<TRequest> : RequestHandler<TRequest> where TRequest : class, IRequest
    {
        public override TRequest Handle(TRequest command)
        {
            return command;
        }
    }
}