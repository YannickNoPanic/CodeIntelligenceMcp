using CodeIntelligenceMcp.JavaScript;
using CodeIntelligenceMcp.JavaScript.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class VueSfcExtractorTests
{
    [Fact]
    public void Extract_WellFormedSfc_ExtractsAllThreeBlocks()
    {
        string content = """
            <template>
              <div>{{ title }}</div>
            </template>

            <script setup lang="ts">
            import { ref } from 'vue'

            const count = ref(0)
            </script>

            <style scoped>
            .button { color: red; }
            </style>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.Blocks.Should().HaveCount(3);
        result.Blocks.Select(b => b.Tag).Should().Equal("template", "script", "style");
        result.Blocks[0].LineStart.Should().Be(1);
        result.Blocks[0].Content.Should().Contain("{{ title }}");
        result.Blocks[1].Lang.Should().Be("ts");
        result.Blocks[2].Content.Should().Contain(".button");
    }

    [Fact]
    public void Extract_ScriptSetup_MarksSetupBlock()
    {
        string content = """
            <script setup>
            const x = 1
            </script>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.Blocks.Should().ContainSingle();
        result.Blocks[0].IsSetup.Should().BeTrue();
    }

    [Fact]
    public void Extract_ClassicScript_IsNotMarkedSetup()
    {
        string content = """
            <script>
            export default {
              name: 'MyComponent'
            }
            </script>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.Blocks.Should().ContainSingle();
        result.Blocks[0].IsSetup.Should().BeFalse();
    }

    [Fact]
    public void Extract_DefinePropsObjectSyntax_ExtractsPropNames()
    {
        string content = """
            <script setup>
            const props = defineProps({
              title: String,
              count: Number
            })
            </script>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.Props.Should().Equal("title", "count");
    }

    [Fact]
    public void Extract_DefinePropsArraySyntax_ExtractsPropNames()
    {
        string content = """
            <script setup>
            const props = defineProps(['title', 'active'])
            </script>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.Props.Should().Equal("title", "active");
    }

    [Fact]
    public void Extract_DefineEmits_ExtractsEmitNames()
    {
        string content = """
            <script setup>
            const emit = defineEmits(['update:modelValue', 'close'])
            </script>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.Emits.Should().Equal("update:modelValue", "close");
    }

    [Fact]
    public void Extract_ComposableCalls_ExtractsComposableNames()
    {
        string content = """
            <script setup>
            import { useRouter } from 'vue-router'

            const router = useRouter()
            const { total } = useCounter()
            </script>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.Composables.Should().Equal("useCounter", "useRouter");
    }

    [Fact]
    public void Extract_ScriptContent_ParsesJsSymbols()
    {
        string content = """
            <template>
              <div />
            </template>

            <script setup lang="ts">
            import { ref } from 'vue'

            function increment() {
              return 1
            }

            const decrement = () => 0
            </script>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.ScriptAnalysis.Should().NotBeNull();
        result.ScriptAnalysis!.Functions.Select(f => f.Name).Should().Equal("increment", "decrement");
        result.ScriptAnalysis.Imports.Should().ContainSingle()
            .Which.Source.Should().Be("vue");
    }

    [Fact]
    public void Extract_NoScriptBlock_ReturnsNullScriptAnalysis()
    {
        string content = """
            <template>
              <span>static</span>
            </template>
            """;

        VueSfcInfo result = VueSfcExtractor.Extract("Component.vue", content);

        result.ScriptAnalysis.Should().BeNull();
        result.Props.Should().BeEmpty();
        result.Emits.Should().BeEmpty();
    }
}
