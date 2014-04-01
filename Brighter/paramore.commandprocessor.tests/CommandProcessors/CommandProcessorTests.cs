using System;
using System.Linq;
using FakeItEasy;
using Machine.Specifications;
using Polly;
using Polly.CircuitBreaker;
using RabbitMQ.Client.Exceptions;
using TinyIoC;
using paramore.brighter.commandprocessor;
using paramore.brighter.commandprocessor.ioccontainers.Adapters;
using paramore.commandprocessor.tests.CommandProcessors.TestDoubles;

namespace paramore.commandprocessor.tests.CommandProcessors
{
    [Subject("Basic send of a command")]
    public class When_sending_a_command_to_the_processor
    {
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();

        Establish context = () =>
        {
            var container = new TinyIoCAdapter(new TinyIoCContainer());
            container.Register<IHandleRequests<MyCommand>, MyCommandHandler>().AsMultiInstance();
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory());

        };

        Because of = () => commandProcessor.Send(myCommand);

        It should_send_the_command_to_the_command_handler = () => MyCommandHandler.ShouldRecieve(myCommand).ShouldBeTrue();
    }

    [Subject("Commands should only have one handler")]
    public class When_there_are_multiple_possible_command_handlers
    {
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();
        private static Exception exception;

        Establish context = () =>
        {
            var container = new TinyIoCAdapter(new TinyIoCContainer());
            container.Register<IHandleRequests<MyCommand>, MyCommandHandler>("DefaultHandler").AsMultiInstance();
            container.Register<IHandleRequests<MyCommand>, MyImplicitHandler>("Implicit Handler").AsMultiInstance();
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory());

        };

        Because of = () =>  exception = Catch.Exception(() => commandProcessor.Send(myCommand));

        It should_fail_because_multiple_recievers_found = () => exception.ShouldBeAssignableTo(typeof (ArgumentException));
        It should_have_an_error_message_that_tells_you_why = () => exception.ShouldContainErrorMessage("More than one handler was found for the typeof command paramore.commandprocessor.tests.CommandProcessors.TestDoubles.MyCommand - a command should only have one handler.");
    }

    [Subject("Commands should have at least one handler")]
    public class When_there_are_no_command_handlers
    {
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();
        private static Exception exception;

        Establish context = () =>
        {
            var container = new TinyIoCAdapter(new TinyIoCContainer());
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory());

        };

        Because of = () =>  exception = Catch.Exception(() => commandProcessor.Send(myCommand));

        It should_fail_because_multiple_recievers_found = () => exception.ShouldBeAssignableTo(typeof (ArgumentException));
        It should_have_an_error_message_that_tells_you_why = () => exception.ShouldContainErrorMessage("No command handler was found for the typeof command paramore.commandprocessor.tests.CommandProcessors.TestDoubles.MyCommand - a command should have only one handler."); 
    }

    [Subject("Basic event publishing")]
    public class When_publishing_an_event_to_the_processor
    {
        static CommandProcessor commandProcessor;
        static readonly MyEvent myEvent = new MyEvent();

        Establish context = () =>
        {
            var container = new TinyIoCAdapter(new TinyIoCContainer());
            container.Register<IHandleRequests<MyEvent>, MyEventHandler>().AsMultiInstance();
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory());
        };

        Because of = () => commandProcessor.Publish(myEvent);

        It should_publish_the_command_to_the_event_handlers = () => MyEventHandler.ShouldRecieve(myEvent).ShouldBeTrue();
    }

    [Subject("An event may have no subscribers")]
    public class When_there_are_no_subscribers
    {
        static CommandProcessor commandProcessor;
        static readonly MyEvent myEvent = new MyEvent();
        static Exception exception;

        Establish context = () =>
        {
            commandProcessor = new CommandProcessor(new TinyIoCAdapter(new TinyIoCContainer()), new InMemoryRequestContextFactory());                                    
        };

        Because of = () => exception = Catch.Exception(() => commandProcessor.Publish(myEvent));

        It should_not_throw_an_exception = () => exception.ShouldBeNull();
    }

    [Subject("An event with multiple subscribers")]
    public class When_there_are_multiple_subscribers
    {
        static CommandProcessor commandProcessor;
        static readonly MyEvent myEvent = new MyEvent();
        static Exception exception;

        Establish context = () =>
                                {
                                    var container = new TinyIoCAdapter(new TinyIoCContainer());
                                    container.Register<IHandleRequests<MyEvent>, MyEventHandler>("My Event Handler").AsMultiInstance();
                                    container.Register<IHandleRequests<MyEvent>, MyOtherEventHandler>("My Other Event Handler").AsMultiInstance();
                                    commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory());
                                };

        Because of = () => exception = Catch.Exception(() => commandProcessor.Publish(myEvent));

        It should_not_throw_an_exception = () => exception.ShouldBeNull();
        It should_publish_the_command_to_the_first_event_handler = () => MyEventHandler.ShouldRecieve(myEvent).ShouldBeTrue(); 
        It should_publish_the_command_to_the_second_event_handler = () => MyOtherEventHandler.ShouldRecieve(myEvent).ShouldBeTrue();
    }


    public class When_using_decoupled_invocation_to_send_a_message_asynchronously
    {
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();
        static Message message;
        static IAmAMessageStore<Message> commandRepository;
        static IAmAMessagingGateway messagingGateway ;
        static IAmAMessageMapper<MyCommand, Message> myCommandMessageMapper;
        static IAdaptAnInversionOfControlContainer container;
        
        Establish context = () =>
        {
            myCommand.Value = "Hello World";
            container = A.Fake<IAdaptAnInversionOfControlContainer>();
            commandRepository = A.Fake<IAmAMessageStore<Message>>();
            messagingGateway = A.Fake<IAmAMessagingGateway>();
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory(), commandRepository, messagingGateway);
            message = new Message(
                header: new MessageHeader(messageId: myCommand.Id, topic: "MyCommand"),
                body: new MessageBody(string.Format("id:{0}, value:{1} ", myCommand.Id, myCommand.Value))
                );
            A.CallTo(() => container.GetInstance<IAmAMessageMapper<MyCommand, Message>>()).Returns( new MyCommandMessageMapper());
        };

        Because of = () => commandProcessor.Post(myCommand);

        It should_store_the_message_in_the_sent_command_message_repository = () => commandRepository.Add(message);
        It should_send_a_message_via_the_messaging_gateway = () => A.CallTo(() => messagingGateway.SendMessage(message)).MustHaveHappened();
    }

    public class When_resending_a_message_asynchronously
    {
        
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();
        static Message message;
        static IAmAMessageStore<Message> commandRepository;
        static IAmAMessagingGateway messagingGateway ;
        
        Establish context = () =>
        {
            myCommand.Value = "Hello World";
            var container = new TinyIoCAdapter(new TinyIoCContainer());
            commandRepository = A.Fake<IAmAMessageStore<Message>>();
            messagingGateway = A.Fake<IAmAMessagingGateway>();
            message = new Message(
                header: new MessageHeader(messageId: myCommand.Id, topic: "MyCommand"),
                body: new MessageBody(string.Format("id:{0}, value:{1} ", myCommand.Id, myCommand.Value))
                );
            A.CallTo(() => commandRepository.Get(message.Header.Id)).Returns(message);
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory(), commandRepository, messagingGateway);
        };

        Because of = () => commandProcessor.Repost(message.Header.Id);

        It should_send_a_message_via_the_messaging_gateway = () => A.CallTo(() => messagingGateway.SendMessage(message)).MustHaveHappened();
    }

    public class When_an_exception_is_thrown_terminate_the_pipeline
    {
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();
        static Exception exception;

        Establish context = () =>
        {
            var container = new TinyIoCAdapter(new TinyIoCContainer());
            container.Register<IHandleRequests<MyCommand>, MyUnusedCommandHandler>().AsMultiInstance();
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory());
        };

        Because of = () => exception = Catch.Exception(() => commandProcessor.Send(myCommand));

        It should_throw_an_exception = () => exception.ShouldNotBeNull();
        It should_fail_the_pipeline_not_execute_it = () => MyUnusedCommandHandler.ShouldRecieve(myCommand).ShouldBeFalse();
    }

    public class When_there_are_no_failures_execute_all_the_steps_in_the_pipeline
    {
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();

        Establish context = () =>
        {
            var container = new TinyIoCAdapter(new TinyIoCContainer());
            container.Register<IHandleRequests<MyCommand>, MyPreAndPostDecoratedHandler>().AsMultiInstance();
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory());

        };

        Because of = () => commandProcessor.Send(myCommand);

        It should_call_the_pre_validation_handler = () => MyValidationHandler<MyCommand>.ShouldRecieve(myCommand).ShouldBeTrue();
        It should_send_the_command_to_the_command_handler = () => MyPreAndPostDecoratedHandler.ShouldRecieve(myCommand).ShouldBeTrue();
        It should_call_the_post_validation_handler = () => MyLoggingHandler<MyCommand>.ShouldRecieve(myCommand).ShouldBeTrue();
    }


    public class When_using_decoupled_invocation_messaging_gateway_throws_an_error_retry_n_times_then_break_circuit
    {
        static CommandProcessor commandProcessor;
        static readonly MyCommand myCommand = new MyCommand();
        static Message message;
        static IAmAMessageStore<Message> commandRepository;
        static IAmAMessagingGateway messagingGateway ;
        static IAmAMessageMapper<MyCommand, Message> myCommandMessageMapper;
        static IAdaptAnInversionOfControlContainer container;
        static Exception failedException;
        static Exception circuitBrokenException;
        
        Establish context = () =>
        {
            myCommand.Value = "Hello World";
            container = A.Fake<IAdaptAnInversionOfControlContainer>();
            commandRepository = A.Fake<IAmAMessageStore<Message>>();
            messagingGateway = A.Fake<IAmAMessagingGateway>();
            commandProcessor = new CommandProcessor(container, new InMemoryRequestContextFactory(), commandRepository, messagingGateway);
            message = new Message(
                header: new MessageHeader(messageId: myCommand.Id, topic: "MyCommand"),
                body: new MessageBody(string.Format("id:{0}, value:{1} ", myCommand.Id, myCommand.Value))
                );

            A.CallTo(() => container.GetInstance<IAmAMessageMapper<MyCommand, Message>>()).Returns( new MyCommandMessageMapper());
            A.CallTo(() => messagingGateway.SendMessage(message)).Throws<Exception>().NumberOfTimes(4);
        };

        Because of = () =>
            {
                //break circuit with retries
                failedException = Catch.Exception(() => commandProcessor.Post(myCommand));
                //now resond with broken ciruit
                circuitBrokenException = (BrokenCircuitException) Catch.Exception(() => commandProcessor.Post(myCommand));
            };

        //It should_send_a_message_via_the_messaging_gateway = () => A.CallTo(() => messagingGateway.SendMessage(message)).MustHaveHappened(Repeated.Exactly.Times(4));
        //It should_throw_a_exception_out_once_all_retries_exhausted = () => failedException.ShouldBeOfExactType(typeof(Exception));
        It should_throw_a_circuit_broken_exception_once_circuit_broken = () => circuitBrokenException.ShouldBeOfExactType(typeof(BrokenCircuitException));
    }


    public class SanityCheck
    {
        Because of = () =>
            {
                var circuitBreakerPolicy = Policy
                    .Handle<Exception>()
                    .CircuitBreaker(1, TimeSpan.FromMilliseconds(3000));

                try
                {
                    circuitBreakerPolicy.Execute(() => func());
                }
                catch (Exception e)
                {
                    int i = 1; //noop
                }

                try
                {
                    circuitBreakerPolicy.Execute(() => func());
                }
                catch (Exception e)
                {
                    int i = 1; //noop
                }
            };

        static void func()
        {
            throw new Exception();
        }

        private It should_have_happend = () => true.ShouldBeTrue();
    }

}

    
