// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using Microsoft.Scripting;
using Microsoft.Scripting.Ast;
using Microsoft.Scripting.Interpreter;

using AstUtils = Microsoft.Scripting.Ast.Utils;
using MSAst = System.Linq.Expressions;

namespace IronPython.Compiler.Ast {
    using Ast = MSAst.Expression;

    public class IfStatement : Statement, IInstructionProvider {
        private readonly IfStatementTest[] _tests;

        public IfStatement(IfStatementTest[] tests, Statement else_) {
            _tests = tests;
            ElseStatement = else_;
        }

        public IList<IfStatementTest> Tests => _tests;

        public Statement ElseStatement { get; }

        public override MSAst.Expression Reduce() {
            return ReduceWorker(true);
        }

        #region IInstructionProvider Members

        void IInstructionProvider.AddInstructions(LightCompiler compiler) {
            // optimizing bool conversions does no good in the light compiler
            compiler.Compile(ReduceWorker(false));
        }

        #endregion

        private MSAst.Expression ReduceWorker(bool optimizeDynamicConvert) {
            MSAst.Expression result;

            if (_tests.Length > 100) {
                // generate:
                // if(x) {
                //   body
                //   goto end
                // } else { 
                // }
                // elseBody
                // end:
                //
                // to avoid deeply recursive trees which can stack overflow.
                BlockBuilder builder = new BlockBuilder();
                var label = Ast.Label();
                for (int i = 0; i < _tests.Length; i++) {
                    IfStatementTest ist = _tests[i];

                    builder.Add(
                        Ast.Condition(
                            optimizeDynamicConvert ?
                                TransformAndDynamicConvert(ist.Test, typeof(bool)) :
                                GlobalParent.Convert(typeof(bool), Microsoft.Scripting.Actions.ConversionResultKind.ExplicitCast, ist.Test),
                            Ast.Block(
                                TransformMaybeSingleLineSuite(ist.Body, GlobalParent.IndexToLocation(ist.Test.StartIndex)),
                                Ast.Goto(label)
                            ),
                            Utils.Empty()
                        )
                    );
                }

                if (ElseStatement != null) {
                    builder.Add(ElseStatement);
                }

                builder.Add(Ast.Label(label));
                result = builder.ToExpression();
            } else {
                // Now build from the inside out
                if (ElseStatement != null) {
                    result = ElseStatement;
                } else {
                    result = AstUtils.Empty();
                }

                int i = _tests.Length;
                while (i-- > 0) {
                    IfStatementTest ist = _tests[i];

                    result = GlobalParent.AddDebugInfoAndVoid(
                        Ast.Condition(
                            optimizeDynamicConvert ?
                                TransformAndDynamicConvert(ist.Test, typeof(bool)) :
                                GlobalParent.Convert(typeof(bool), Microsoft.Scripting.Actions.ConversionResultKind.ExplicitCast, ist.Test),
                            TransformMaybeSingleLineSuite(ist.Body, GlobalParent.IndexToLocation(ist.Test.StartIndex)),
                            result
                        ),
                        new SourceSpan(GlobalParent.IndexToLocation(ist.StartIndex), GlobalParent.IndexToLocation(ist.HeaderIndex))
                    );
                }
            }

            return result;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_tests != null) {
                    foreach (IfStatementTest test in _tests) {
                        test.Walk(walker);
                    }
                }
                ElseStatement?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
