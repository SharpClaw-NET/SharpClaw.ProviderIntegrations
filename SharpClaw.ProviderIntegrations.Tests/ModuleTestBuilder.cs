using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.ProviderIntegrations.Tests;

internal sealed class ModuleTestBuilder : ISharpClawModuleBuilder
{
    public ModuleTestBuilder(IServiceCollection services)
    {
        Services = services;
        Storage = new StorageBuilder();
    }

    public IServiceCollection Services { get; }
    public IModuleContractBuilder Contracts { get; } = new NoOpContractBuilder();
    public IModuleStorageBuilder Storage { get; }
    public IActionDefinitionBuilder Actions { get; } = new NoOpActionDefinitionBuilder();
    public IActionHookBuilder Hooks { get; } = new NoOpActionHookBuilder();
    public IEventDefinitionBuilder Events { get; } = new NoOpEventDefinitionBuilder();
    public IToolContributionBuilder Tools { get; } = new NoOpToolBuilder();
    public IChatLifecycleBuilder Chat { get; } = new NoOpChatBuilder();

    public IReadOnlyList<ModuleStorageContractDescriptor> StorageContracts =>
        ((StorageBuilder)Storage).Contracts;

    public static ModuleTestBuilder For(IServiceCollection services) =>
        new(services);

    private sealed class NoOpContractBuilder : IModuleContractBuilder
    {
        public void Export<T>(string contractName, int schemaVersion = 1, int maxBytes = 65_536) { }

        public void Require<T>(
            string contractName,
            int minimumSchemaVersion = 1,
            bool optional = false) { }
    }

    private sealed class StorageBuilder : IModuleStorageBuilder
    {
        public List<ModuleStorageContractDescriptor> Contracts { get; } = [];

        public void Add(ModuleStorageContractDescriptor contract) =>
            Contracts.Add(contract);
    }

    private sealed class NoOpActionDefinitionBuilder : IActionDefinitionBuilder
    {
        public void Add<TAction, TResult>(ActionDescriptor<TAction, TResult> descriptor) { }
    }

    private sealed class NoOpActionHookBuilder : IActionHookBuilder
    {
        private static readonly IActionHookRegistrationBuilder Registration =
            new NoOpActionHookRegistrationBuilder();

        public IActionHookRegistrationBuilder For(SharpClawActionKey key) => Registration;
        public IActionHookRegistrationBuilder Category(string category) => Registration;
        public IActionHookRegistrationBuilder AnyAction() => Registration;
    }

    private sealed class NoOpActionHookRegistrationBuilder : IActionHookRegistrationBuilder
    {
        public void Use<TInterceptor>(HookOrdering ordering) { }
        public void UseAny<TInterceptor>(HookOrdering ordering) { }
    }

    private sealed class NoOpEventDefinitionBuilder : IEventDefinitionBuilder
    {
        private static readonly IEventHookRegistrationBuilder Registration =
            new NoOpEventHookRegistrationBuilder();

        public void Add<TEvent>(EventDescriptor<TEvent> descriptor) { }
        public IEventHookRegistrationBuilder For(SharpClawEventKey key) => Registration;
        public IEventHookRegistrationBuilder Category(string category) => Registration;
        public IEventHookRegistrationBuilder AnyEvent() => Registration;
    }

    private sealed class NoOpEventHookRegistrationBuilder : IEventHookRegistrationBuilder
    {
        public void Intercept<TInterceptor>(HookOrdering ordering) { }
        public void InterceptAny<TInterceptor>(HookOrdering ordering) { }
        public void Listen<TListener>(EventDelivery delivery, HookOrdering ordering) { }
        public void ListenAny<TListener>(EventDelivery delivery, HookOrdering ordering) { }
    }

    private sealed class NoOpToolBuilder : IToolContributionBuilder
    {
        public void Add<THandler>(ToolDescriptor descriptor)
            where THandler : IToolHandler { }
    }

    private sealed class NoOpChatBuilder : IChatLifecycleBuilder
    {
        public void UseConversationResolver<TResolver>(ExclusiveRegistration registration)
            where TResolver : IConversationResolver { }

        public void UseChatProfileResolver<TResolver>(ExclusiveRegistration registration)
            where TResolver : IChatProfileResolver { }

        public void AddContextContributor<TContributor>()
            where TContributor : IChatContextContributor { }
    }
}
