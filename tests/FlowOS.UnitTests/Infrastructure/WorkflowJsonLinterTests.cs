using System.Linq;
using FlowOS.Infrastructure.Services;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure
{
    public class WorkflowJsonLinterTests
    {
        private readonly WorkflowJsonLinter _linter = new WorkflowJsonLinter();

        [Fact]
        public void Lint_ValidJson_ReturnsNoErrors()
        {
            var json = @"{
                ""events"": [ { ""eventId"": ""EVT-1"" } ],
                ""stateMachine"": { ""states"": [""A"", ""B""], ""transitions"": [{ ""fromState"": ""A"", ""toState"": ""B"", ""eventId"": ""EVT-1"" }] },
                ""workflow"": { ""startStepId"": ""S1"", ""steps"": [ { ""stepId"": ""S1"", ""stepType"": ""Command"", ""nextSteps"": { ""EVT-1"": ""END"" } } ] }
            }";

            var errors = _linter.Lint(json);
            Assert.Empty(errors);
        }

        [Fact]
        public void Lint_MissingSections_ReturnsStructuralErrors()
        {
            var json = "{}";
            var errors = _linter.Lint(json);

            Assert.Contains(errors, e => e.Code == "STR-001"); // Missing events
            Assert.Contains(errors, e => e.Code == "STR-002"); // Missing stateMachine
            Assert.Contains(errors, e => e.Code == "STR-003"); // Missing workflow
        }

        [Fact]
        public void Lint_DuplicateEventIds_ReturnsErrorWithLineNumber()
        {
            var json = @"{
                ""events"": [
                    { ""eventId"": ""EVT-1"" },
                    { ""eventId"": ""EVT-1"" }
                ],
                ""stateMachine"": {},
                ""workflow"": {}
            }";

            var errors = _linter.Lint(json).ToList();
            var err = errors.FirstOrDefault(e => e.Code == "EVT-003");
            
            Assert.NotNull(err);
            Assert.True(err.Line > 0); // Should have line number
        }

        [Fact]
        public void Lint_InvalidTransitionReference_ReturnsConsistencyError()
        {
            var json = @"{
                ""events"": [ { ""eventId"": ""EVT-1"" } ],
                ""stateMachine"": {
                    ""states"": [""A""],
                    ""transitions"": [
                        { ""fromState"": ""A"", ""toState"": ""B"", ""eventId"": ""EVT-1"" }
                    ]
                },
                ""workflow"": {}
            }";

            var errors = _linter.Lint(json);
            Assert.Contains(errors, e => e.Code == "SM-003"); // Unknown ToState 'B'
        }
    }
}
