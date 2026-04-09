using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using RoslynTypeInfo = CodeIntelligenceMcp.Roslyn.Models.TypeInfo;

namespace CodeIntelligenceMcp.Tests;

public sealed class RoslynLookupTests
{
    private static readonly CleanArchitectureNames CleanArch = new("MyApp.Core", "MyApp.Infrastructure", "MyApp.Web");

    private static RoslynWorkspaceIndex BuildIndex(params (string source, string projectName, string fileName)[] sources)
    {
        IEnumerable<(Compilation, string)> compilations = sources
            .GroupBy(s => s.projectName)
            .Select(group =>
            {
                IEnumerable<SyntaxTree> trees = group.Select(s =>
                    CSharpSyntaxTree.ParseText(s.source, path: s.fileName));

                Compilation compilation = CSharpCompilation.Create(
                    group.Key,
                    trees,
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                return (compilation, group.Key);
            });

        return RoslynWorkspaceIndex.CreateForTesting(compilations, CleanArch);
    }

    [Fact]
    public void GetType_ReturnsType_BySimpleName()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public class OrderService { }",
            "MyApp.Core",
            "OrderService.cs"));

        RoslynTypeInfo? result = index.GetType("OrderService");

        result.Should().NotBeNull();
        result!.Name.Should().Be("OrderService");
        result.Namespace.Should().Be("MyApp.Core");
        result.Kind.Should().Be("class");
    }

    [Fact]
    public void GetType_ReturnsType_ByFullyQualifiedName()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public class OrderService { }",
            "MyApp.Core",
            "OrderService.cs"));

        RoslynTypeInfo? result = index.GetType("MyApp.Core.OrderService");

        result.Should().NotBeNull();
        result!.Name.Should().Be("OrderService");
    }

    [Fact]
    public void GetType_ReturnsNull_WhenTypeNotFound()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public class OrderService { }",
            "MyApp.Core",
            "OrderService.cs"));

        RoslynTypeInfo? result = index.GetType("NonExistentType");

        result.Should().BeNull();
    }

    [Fact]
    public void GetType_ReturnsCorrectKind_ForRecord()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public record CreateOrderRequest(string Name);",
            "MyApp.Core",
            "CreateOrderRequest.cs"));

        RoslynTypeInfo? result = index.GetType("CreateOrderRequest");

        result.Should().NotBeNull();
        result!.Kind.Should().Be("record");
    }

    [Fact]
    public void GetType_ReturnsCorrectKind_ForInterface()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IOrderRepository { }",
            "MyApp.Core",
            "IOrderRepository.cs"));

        RoslynTypeInfo? result = index.GetType("IOrderRepository");

        result.Should().NotBeNull();
        result!.Kind.Should().Be("interface");
    }

    [Fact]
    public void GetType_IncludesProperties()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public class Order { public string Name { get; set; } = \"\"; public int Quantity { get; set; } }",
            "MyApp.Core",
            "Order.cs"));

        RoslynTypeInfo? result = index.GetType("Order");

        result.Should().NotBeNull();
        result!.Properties.Should().Contain(p => p.Name == "Name" && p.Type == "string");
        result.Properties.Should().Contain(p => p.Name == "Quantity" && p.Type == "int");
    }

    [Fact]
    public void GetType_IncludesConstructorParameters()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IRepo {} public class OrderService { public OrderService(IRepo repo, string name) {} }",
            "MyApp.Core",
            "OrderService.cs"));

        RoslynTypeInfo? result = index.GetType("OrderService");

        result.Should().NotBeNull();
        result!.ConstructorParameters.Should().HaveCount(2);
        result.ConstructorParameters.Should().Contain(p => p.Name == "repo");
        result.ConstructorParameters.Should().Contain(p => p.Name == "name");
    }

    [Fact]
    public void FindTypes_FiltersByNameContains()
    {
        RoslynWorkspaceIndex index = BuildIndex(
            ("namespace MyApp.Core; public class OrderService { }", "MyApp.Core", "OrderService.cs"),
            ("namespace MyApp.Core; public class CustomerService { }", "MyApp.Core", "CustomerService.cs"),
            ("namespace MyApp.Core; public class OrderRepository { }", "MyApp.Core", "OrderRepository.cs"));

        IReadOnlyList<TypeSummary> result = index.FindTypes(nameContains: "Order");

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "OrderService");
        result.Should().Contain(t => t.Name == "OrderRepository");
    }

    [Fact]
    public void FindTypes_FiltersByNamespace()
    {
        RoslynWorkspaceIndex index = BuildIndex(
            ("namespace MyApp.Core; public class OrderService { }", "MyApp.Core", "OrderService.cs"),
            ("namespace MyApp.Infrastructure; public class OrderRepository { }", "MyApp.Infrastructure", "OrderRepository.cs"));

        IReadOnlyList<TypeSummary> result = index.FindTypes(@namespace: "MyApp.Core");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("OrderService");
    }

    [Fact]
    public void FindTypes_FiltersByKind()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IOrderService {} public class OrderService {} public record OrderId(int Value);",
            "MyApp.Core",
            "Types.cs"));

        IReadOnlyList<TypeSummary> interfaces = index.FindTypes(kind: "interface");
        IReadOnlyList<TypeSummary> records = index.FindTypes(kind: "record");

        interfaces.Should().ContainSingle(t => t.Name == "IOrderService");
        records.Should().ContainSingle(t => t.Name == "OrderId");
    }

    [Fact]
    public void FindTypes_FiltersByImplementsInterface()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IUseCase<T> {} public class GetOrderUseCase : IUseCase<string> {} public class OrderService {}",
            "MyApp.Core",
            "Types.cs"));

        IReadOnlyList<TypeSummary> result = index.FindTypes(implementsInterface: "IUseCase");

        result.Should().ContainSingle(t => t.Name == "GetOrderUseCase");
    }

    [Fact]
    public void FindImplementations_ReturnsAllImplementors()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IOrderRepository {} public class SqlOrderRepository : IOrderRepository {} public class InMemoryOrderRepository : IOrderRepository {} public class OrderService {}",
            "MyApp.Core",
            "Types.cs"));

        IReadOnlyList<ImplementationSummary> result = index.FindImplementations("IOrderRepository");

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.TypeName == "SqlOrderRepository");
        result.Should().Contain(t => t.TypeName == "InMemoryOrderRepository");
    }

    [Fact]
    public void FindImplementations_ExcludesInterfaces()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IBase {} public interface IDerived : IBase {} public class Concrete : IDerived {}",
            "MyApp.Core",
            "Types.cs"));

        IReadOnlyList<ImplementationSummary> result = index.FindImplementations("IBase");

        result.Should().NotContain(t => t.TypeName == "IDerived");
        result.Should().Contain(t => t.TypeName == "Concrete");
    }

    [Fact]
    public void GetDependencies_ReturnsConstructorParameters()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IRepo {} public interface ILogger {} public class OrderService(IRepo repo, ILogger logger) {}",
            "MyApp.Core",
            "OrderService.cs"));

        DependencyInfo? result = index.GetDependencies("OrderService");

        result.Should().NotBeNull();
        result!.TypeName.Should().Be("OrderService");
        result.ConstructorParameters.Should().HaveCount(2);
        result.ConstructorParameters.Should().Contain(p => p.Name == "repo" && p.Type == "MyApp.Core.IRepo");
        result.ConstructorParameters.Should().Contain(p => p.Name == "logger" && p.Type == "MyApp.Core.ILogger");
    }

    [Fact]
    public void GetDependencies_ReturnsNull_WhenTypeNotFound()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public class OrderService {}",
            "MyApp.Core",
            "OrderService.cs"));

        DependencyInfo? result = index.GetDependencies("NonExistent");

        result.Should().BeNull();
    }

    [Fact]
    public void GetPublicSurface_SegregatesTypesByKind()
    {
        RoslynWorkspaceIndex index = BuildIndex((
            "namespace MyApp.Core; public interface IOrderService {} public class OrderService {} public record OrderId(int Value); public enum OrderStatus { Pending, Complete }",
            "MyApp.Core",
            "Types.cs"));

        PublicSurface result = index.GetPublicSurface("MyApp.Core");

        result.Interfaces.Should().ContainSingle(i => i.Name == "IOrderService");
        result.PublicClasses.Should().ContainSingle(c => c.Name == "OrderService");
        result.PublicRecords.Should().ContainSingle(r => r.Name == "OrderId");
        result.Enums.Should().ContainSingle(e => e.Name == "OrderStatus");
    }

    [Fact]
    public void SearchSymbol_FindsTypesBySubstring()
    {
        RoslynWorkspaceIndex index = BuildIndex(
            ("namespace MyApp.Core; public class GetOrderUseCase {}", "MyApp.Core", "GetOrderUseCase.cs"),
            ("namespace MyApp.Core; public class CreateOrderUseCase {}", "MyApp.Core", "CreateOrderUseCase.cs"),
            ("namespace MyApp.Core; public class CustomerService {}", "MyApp.Core", "CustomerService.cs"));

        IReadOnlyList<SymbolSearchResult> result = index.SearchSymbol("UseCase");

        result.Should().Contain(r => r.SymbolName == "GetOrderUseCase");
        result.Should().Contain(r => r.SymbolName == "CreateOrderUseCase");
        result.Should().NotContain(r => r.SymbolName == "CustomerService");
    }
}
