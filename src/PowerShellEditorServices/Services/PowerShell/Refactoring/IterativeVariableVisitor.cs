// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Management.Automation.Language;
using Microsoft.PowerShell.EditorServices.Handlers;
using System.Linq;

namespace Microsoft.PowerShell.EditorServices.Refactoring
{

    internal class VariableRenameIterative
    {
        private readonly string OldName;
        private readonly string NewName;
        internal Stack<Ast> ScopeStack = new();
        internal bool ShouldRename;
        public List<TextChange> Modifications = new();
        internal int StartLineNumber;
        internal int StartColumnNumber;
        internal VariableExpressionAst TargetVariableAst;
        internal VariableExpressionAst DuplicateVariableAst;
        internal List<string> dotSourcedScripts = new();
        internal readonly Ast ScriptAst;
        internal bool isParam;
        internal List<string> Log = new();

        public VariableRenameIterative(string NewName, int StartLineNumber, int StartColumnNumber, Ast ScriptAst)
        {
            this.NewName = NewName;
            this.StartLineNumber = StartLineNumber;
            this.StartColumnNumber = StartColumnNumber;
            this.ScriptAst = ScriptAst;

            VariableExpressionAst Node = (VariableExpressionAst)VariableRename.GetVariableTopAssignment(StartLineNumber, StartColumnNumber, ScriptAst);
            if (Node != null)
            {
                if (Node.Parent is ParameterAst)
                {
                    isParam = true;
                }
                TargetVariableAst = Node;
                OldName = TargetVariableAst.VariablePath.UserPath.Replace("$", "");
                this.StartColumnNumber = TargetVariableAst.Extent.StartColumnNumber;
                this.StartLineNumber = TargetVariableAst.Extent.StartLineNumber;
            }
        }

        public static Ast GetAstNodeByLineAndColumn(int StartLineNumber, int StartColumnNumber, Ast ScriptAst)
        {
            Ast result = null;
            result = ScriptAst.Find(ast =>
            {
                return ast.Extent.StartLineNumber == StartLineNumber &&
                ast.Extent.StartColumnNumber == StartColumnNumber &&
                ast is VariableExpressionAst or CommandParameterAst;
            }, true);
            if (result == null)
            {
                throw new TargetSymbolNotFoundException();
            }
            return result;
        }

        public static Ast GetVariableTopAssignment(int StartLineNumber, int StartColumnNumber, Ast ScriptAst)
        {

            // Look up the target object
            Ast node = GetAstNodeByLineAndColumn(StartLineNumber, StartColumnNumber, ScriptAst);

            string name = node is CommandParameterAst commdef
                ? commdef.ParameterName
                : node is VariableExpressionAst varDef ? varDef.VariablePath.UserPath : throw new TargetSymbolNotFoundException();

            Ast TargetParent = GetAstParentScope(node);

            List<VariableExpressionAst> VariableAssignments = ScriptAst.FindAll(ast =>
            {
                return ast is VariableExpressionAst VarDef &&
                VarDef.Parent is AssignmentStatementAst or ParameterAst &&
                VarDef.VariablePath.UserPath.ToLower() == name.ToLower() &&
                (VarDef.Extent.EndLineNumber < node.Extent.StartLineNumber ||
                (VarDef.Extent.EndColumnNumber <= node.Extent.StartColumnNumber &&
                VarDef.Extent.EndLineNumber <= node.Extent.StartLineNumber));
            }, true).Cast<VariableExpressionAst>().ToList();
            // return the def if we have no matches
            if (VariableAssignments.Count == 0)
            {
                return node;
            }
            Ast CorrectDefinition = null;
            for (int i = VariableAssignments.Count - 1; i >= 0; i--)
            {
                VariableExpressionAst element = VariableAssignments[i];

                Ast parent = GetAstParentScope(element);
                // closest assignment statement is within the scope of the node
                if (TargetParent == parent)
                {
                    CorrectDefinition = element;
                    break;
                }
                else if (node.Parent is AssignmentStatementAst)
                {
                    // the node is probably the first assignment statement within the scope
                    CorrectDefinition = node;
                    break;
                }
                // node is proably just a reference of an assignment statement within the global scope or higher
                if (node.Parent is not AssignmentStatementAst)
                {
                    if (null == parent || null == parent.Parent)
                    {
                        // we have hit the global scope of the script file
                        CorrectDefinition = element;
                        break;
                    }
                    if (parent is FunctionDefinitionAst funcDef && node is CommandParameterAst)
                    {
                        if (node.Parent is CommandAst commDef)
                        {
                            if (funcDef.Name == commDef.GetCommandName()
                            && funcDef.Parent.Parent == TargetParent)
                            {
                                CorrectDefinition = element;
                                break;
                            }
                        }
                    }
                    if (WithinTargetsScope(element, node))
                    {
                        CorrectDefinition = element;
                    }
                }


            }
            return CorrectDefinition ?? node;
        }

        internal static Ast GetAstParentScope(Ast node)
        {
            Ast parent = node;
            // Walk backwards up the tree look
            while (parent != null)
            {
                if (parent is ScriptBlockAst or FunctionDefinitionAst)
                {
                    break;
                }
                parent = parent.Parent;
            }
            if (parent is ScriptBlockAst && parent.Parent != null && parent.Parent is FunctionDefinitionAst)
            {
                parent = parent.Parent;
            }
            return parent;
        }

        internal static bool WithinTargetsScope(Ast Target, Ast Child)
        {
            bool r = false;
            Ast childParent = Child.Parent;
            Ast TargetScope = GetAstParentScope(Target);
            while (childParent != null)
            {
                if (childParent is FunctionDefinitionAst)
                {
                    break;
                }
                if (childParent == TargetScope)
                {
                    break;
                }
                childParent = childParent.Parent;
            }
            if (childParent == TargetScope)
            {
                r = true;
            }
            return r;
        }

        public class NodeProcessingState
        {
            public Ast Node { get; set; }
            public IEnumerator<Ast> ChildrenEnumerator { get; set; }
        }

        public void Visit(Ast root)
        {
            Stack<NodeProcessingState> processingStack = new();

            processingStack.Push(new NodeProcessingState { Node = root});

            while (processingStack.Count > 0)
            {
                NodeProcessingState currentState = processingStack.Peek();

                if (currentState.ChildrenEnumerator == null)
                {
                    // First time processing this node. Do the initial processing.
                    ProcessNode(currentState.Node);  // This line is crucial.

                    // Get the children and set up the enumerator.
                    IEnumerable<Ast> children = currentState.Node.FindAll(ast => ast.Parent == currentState.Node, searchNestedScriptBlocks: true);
                    currentState.ChildrenEnumerator = children.GetEnumerator();
                }

                // Process the next child.
                if (currentState.ChildrenEnumerator.MoveNext())
                {
                    Ast child = currentState.ChildrenEnumerator.Current;
                    processingStack.Push(new NodeProcessingState { Node = child});
                }
                else
                {
                    // All children have been processed, we're done with this node.
                    processingStack.Pop();
                }
            }
        }

        public void ProcessNode(Ast node)
        {
            Log.Add($"Proc node: {node.GetType().Name}, " +
            $"SL: {node.Extent.StartLineNumber}, " +
            $"SC: {node.Extent.StartColumnNumber}");

            switch (node)
            {
                case CommandParameterAst commandParameterAst:

                    if (commandParameterAst.ParameterName.ToLower() == OldName.ToLower())
                    {
                        if (commandParameterAst.Extent.StartLineNumber == StartLineNumber &&
                            commandParameterAst.Extent.StartColumnNumber == StartColumnNumber)
                        {
                            ShouldRename = true;
                        }

                        if (ShouldRename && isParam)
                        {
                            TextChange Change = new()
                            {
                                NewText = NewName.Contains("-") ? NewName : "-" + NewName,
                                StartLine = commandParameterAst.Extent.StartLineNumber - 1,
                                StartColumn = commandParameterAst.Extent.StartColumnNumber - 1,
                                EndLine = commandParameterAst.Extent.StartLineNumber - 1,
                                EndColumn = commandParameterAst.Extent.StartColumnNumber + OldName.Length,
                            };

                            Modifications.Add(Change);
                        }
                    }
                    break;
                case VariableExpressionAst variableExpressionAst:
                    if (variableExpressionAst.VariablePath.UserPath.ToLower() == OldName.ToLower())
                    {
                        if (variableExpressionAst.Extent.StartColumnNumber == StartColumnNumber &&
                        variableExpressionAst.Extent.StartLineNumber == StartLineNumber)
                        {
                            ShouldRename = true;
                            TargetVariableAst = variableExpressionAst;
                        }else if (variableExpressionAst.Parent is CommandAst commandAst)
                        {
                            if(WithinTargetsScope(TargetVariableAst, commandAst))
                            {
                                ShouldRename = true;
                            }
                        }
                        else if (variableExpressionAst.Parent is AssignmentStatementAst assignment &&
                            assignment.Operator == TokenKind.Equals)
                        {
                            if (!WithinTargetsScope(TargetVariableAst, variableExpressionAst))
                            {
                                DuplicateVariableAst = variableExpressionAst;
                                ShouldRename = false;
                            }

                        }

                        if (ShouldRename)
                        {
                            // have some modifications to account for the dollar sign prefix powershell uses for variables
                            TextChange Change = new()
                            {
                                NewText = NewName.Contains("$") ? NewName : "$" + NewName,
                                StartLine = variableExpressionAst.Extent.StartLineNumber - 1,
                                StartColumn = variableExpressionAst.Extent.StartColumnNumber - 1,
                                EndLine = variableExpressionAst.Extent.StartLineNumber - 1,
                                EndColumn = variableExpressionAst.Extent.StartColumnNumber + OldName.Length,
                            };

                            Modifications.Add(Change);
                        }
                    }
                    break;

            }
            Log.Add($"ShouldRename after proc: {ShouldRename}");
        }

        public static Ast GetAstNodeByLineAndColumn(string OldName, int StartLineNumber, int StartColumnNumber, Ast ScriptFile)
        {
            Ast result = null;
            // Looking for a function
            result = ScriptFile.Find(ast =>
            {
                return ast.Extent.StartLineNumber == StartLineNumber &&
                ast.Extent.StartColumnNumber == StartColumnNumber &&
                ast is FunctionDefinitionAst FuncDef &&
                FuncDef.Name.ToLower() == OldName.ToLower();
            }, true);
            // Looking for a a Command call
            if (null == result)
            {
                result = ScriptFile.Find(ast =>
                {
                    return ast.Extent.StartLineNumber == StartLineNumber &&
                    ast.Extent.StartColumnNumber == StartColumnNumber &&
                    ast is CommandAst CommDef &&
                    CommDef.GetCommandName().ToLower() == OldName.ToLower();
                }, true);
            }

            return result;
        }

        public static FunctionDefinitionAst GetFunctionDefByCommandAst(string OldName, int StartLineNumber, int StartColumnNumber, Ast ScriptFile)
        {
            // Look up the targetted object
            CommandAst TargetCommand = (CommandAst)ScriptFile.Find(ast =>
            {
                return ast is CommandAst CommDef &&
                CommDef.GetCommandName().ToLower() == OldName.ToLower() &&
                CommDef.Extent.StartLineNumber == StartLineNumber &&
                CommDef.Extent.StartColumnNumber == StartColumnNumber;
            }, true);

            string FunctionName = TargetCommand.GetCommandName();

            List<FunctionDefinitionAst> FunctionDefinitions = ScriptFile.FindAll(ast =>
            {
                return ast is FunctionDefinitionAst FuncDef &&
                FuncDef.Name.ToLower() == OldName.ToLower() &&
                (FuncDef.Extent.EndLineNumber < TargetCommand.Extent.StartLineNumber ||
                (FuncDef.Extent.EndColumnNumber <= TargetCommand.Extent.StartColumnNumber &&
                FuncDef.Extent.EndLineNumber <= TargetCommand.Extent.StartLineNumber));
            }, true).Cast<FunctionDefinitionAst>().ToList();
            // return the function def if we only have one match
            if (FunctionDefinitions.Count == 1)
            {
                return FunctionDefinitions[0];
            }
            // Sort function definitions
            //FunctionDefinitions.Sort((a, b) =>
            //{
            //    return b.Extent.EndColumnNumber + b.Extent.EndLineNumber -
            //       a.Extent.EndLineNumber + a.Extent.EndColumnNumber;
            //});
            // Determine which function definition is the right one
            FunctionDefinitionAst CorrectDefinition = null;
            for (int i = FunctionDefinitions.Count - 1; i >= 0; i--)
            {
                FunctionDefinitionAst element = FunctionDefinitions[i];

                Ast parent = element.Parent;
                // walk backwards till we hit a functiondefinition if any
                while (null != parent)
                {
                    if (parent is FunctionDefinitionAst)
                    {
                        break;
                    }
                    parent = parent.Parent;
                }
                // we have hit the global scope of the script file
                if (null == parent)
                {
                    CorrectDefinition = element;
                    break;
                }

                if (TargetCommand.Parent == parent)
                {
                    CorrectDefinition = (FunctionDefinitionAst)parent;
                }
            }
            return CorrectDefinition;
        }
    }
}