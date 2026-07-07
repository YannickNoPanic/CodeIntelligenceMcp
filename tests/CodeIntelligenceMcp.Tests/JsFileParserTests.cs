using CodeIntelligenceMcp.JavaScript;
using CodeIntelligenceMcp.JavaScript.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class JsFileParserTests
{
    [Fact]
    public void Parse_FunctionDeclaration_ExtractsNameAndLineNumber()
    {
        string content = """
            function loadData() {
                return 1;
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().HaveCount(1);
        result.Functions[0].Name.Should().Be("loadData");
        result.Functions[0].LineStart.Should().Be(1);
        result.Functions[0].Kind.Should().Be("declaration");
    }

    [Fact]
    public void Parse_ExportedAsyncFunction_MarksExportedAndAsync()
    {
        string content = """
            export async function fetchUsers() {
                return [];
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().HaveCount(1);
        result.Functions[0].IsExported.Should().BeTrue();
        result.Functions[0].IsAsync.Should().BeTrue();
        result.Exports.Should().ContainSingle(e => e.Name == "fetchUsers" && e.Kind == "function");
    }

    [Fact]
    public void Parse_ArrowFunctionConst_ExtractsArrowKind()
    {
        string content = """
            const handler = async (req, res) => {
                res.send("ok");
            };
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().HaveCount(1);
        result.Functions[0].Name.Should().Be("handler");
        result.Functions[0].Kind.Should().Be("arrow");
        result.Functions[0].IsAsync.Should().BeTrue();
    }

    [Fact]
    public void Parse_ClassWithMethods_ExtractsMethodsAndExtends()
    {
        string content = """
            export class UserService extends BaseService {
                constructor() {
                    super();
                }

                async load(id) {
                    return id;
                }

                save(user) {
                }
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Classes.Should().HaveCount(1);
        JsClassInfo cls = result.Classes[0];
        cls.Name.Should().Be("UserService");
        cls.Extends.Should().Be("BaseService");
        cls.IsExported.Should().BeTrue();
        cls.Methods.Select(m => m.Name).Should().Equal("load", "save");
        cls.Methods[0].IsAsync.Should().BeTrue();
    }

    [Fact]
    public void Parse_EsmImports_ExtractsImportDetails()
    {
        string content = """
            import fs from 'fs';
            import { join, resolve } from 'path';
            import * as utils from './utils';
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Imports.Should().HaveCount(3);
        result.Imports[0].DefaultImport.Should().Be("fs");
        result.Imports[0].Source.Should().Be("fs");
        result.Imports[1].NamedImports.Should().Equal("join", "resolve");
        result.Imports[2].NamespaceImport.Should().Be("utils");
        result.ModuleType.Should().Be("esm");
    }

    [Fact]
    public void Parse_CommonJsRequire_ExtractsImportAndModuleType()
    {
        string content = """
            const path = require('path');
            const { readFile } = require('fs');
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Imports.Should().HaveCount(2);
        result.Imports[0].DefaultImport.Should().Be("path");
        result.Imports[1].NamedImports.Should().Equal("readFile");
        result.ModuleType.Should().Be("commonjs");
    }

    [Fact]
    public void Parse_NamedExports_ExtractsExportNames()
    {
        string content = """
            export { formatDate, parseDate as parse };
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Exports.Should().HaveCount(2);
        result.Exports.Select(e => e.Name).Should().Equal("formatDate", "parseDate");
    }

    [Fact]
    public void Parse_TypeScriptInterface_ExtractsInterfaceInfo()
    {
        string content = """
            export interface UserDto extends BaseDto {
                id: number;
                name: string;
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.ts", content);

        result.Interfaces.Should().HaveCount(1);
        result.Interfaces[0].Name.Should().Be("UserDto");
        result.Interfaces[0].Extends.Should().Equal("BaseDto");
        result.Interfaces[0].IsExported.Should().BeTrue();
    }

    [Fact]
    public void Parse_TypeAliasAndEnum_ExtractsTypeInfo()
    {
        string content = """
            export type UserId = string;

            export enum Status {
                Active,
                Inactive
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.ts", content);

        result.TypeAliases.Should().ContainSingle()
            .Which.Name.Should().Be("UserId");
        result.Enums.Should().ContainSingle()
            .Which.Name.Should().Be("Status");
    }

    [Fact]
    public void Parse_MultiLineBlockCommentWithDeclarations_DoesNotCreatePhantomSymbols()
    {
        string content = """
            /*
            function phantom() {
                return 1;
            }

            class Bar {
                doWork() {
                }
            }
            */
            export function real() {
                return 2;
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("real");
        result.Classes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SingleLineBlockCommentBeforeDeclaration_DetectsDeclaration()
    {
        string content = """
            /* deprecated, use loadV2 */ function load() {
                return 1;
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("load");
    }

    [Fact]
    public void Parse_SingleLineBlockCommentContainingDeclaration_DoesNotCreateSymbol()
    {
        string content = """
            /* function phantom() {} */
            const real = () => 1;
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("real");
    }

    [Fact]
    public void Parse_LineCommentWithDeclaration_DoesNotCreateSymbol()
    {
        string content = """
            // function phantom() {
            // class Phantom {
            function real() {
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("real");
        result.Classes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_JsDocCommentBeforeFunction_DetectsOnlyRealFunction()
    {
        string content = """
            /**
             * Loads a user.
             * Example: function usage() { load(1); }
             * @param {number} id
             */
            function load(id) {
                return id;
            }
            """;

        JsFileInfo result = JsFileParser.Parse("test.js", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("load");
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyCollections()
    {
        JsFileInfo result = JsFileParser.Parse("empty.js", string.Empty);

        result.Functions.Should().BeEmpty();
        result.Classes.Should().BeEmpty();
        result.Imports.Should().BeEmpty();
        result.ModuleType.Should().Be("none");
    }
}
