// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Transactions;

namespace RevitAI.Commands;

/// <summary>
/// Test command for verifying the TransactionManager infrastructure.
/// Runs tests to validate transaction handling, groups, and rollback behavior.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class TestTransactionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc == null)
        {
            TaskDialog.Show("RevitAI Transaction Tests", "No document is open. Please open a Revit document first.");
            return Result.Cancelled;
        }

        var results = new StringBuilder();
        results.AppendLine("Transaction Manager Tests");
        results.AppendLine("=========================");
        results.AppendLine();

        var allPassed = true;
        var manager = TransactionManager.Instance;

        // Ensure no leftover group from previous tests
        manager.EnsureGroupClosed();

        // Test 1: Single transaction with commit
        try
        {
            results.Append("Test 1: Single transaction commit... ");

            using (var scope = manager.StartTransaction(doc, "Test Commit"))
            {
                // Perform a harmless operation (just verifying we can commit)
                scope.Commit();
            }

            results.AppendLine("PASSED");
        }
        catch (Exception ex)
        {
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 2: Single transaction with auto-rollback (dispose without commit)
        try
        {
            results.Append("Test 2: Single transaction auto-rollback... ");

            using (var scope = manager.StartTransaction(doc, "Test Rollback"))
            {
                // Don't commit - should auto-rollback on dispose
            }

            results.AppendLine("PASSED");
        }
        catch (Exception ex)
        {
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 3: Transaction group commit
        try
        {
            results.Append("Test 3: Transaction group commit... ");

            manager.StartGroup(doc, "Test Group Commit");

            if (!manager.IsGroupActive)
            {
                results.AppendLine("FAILED - Group should be active after StartGroup");
                allPassed = false;
            }
            else
            {
                using (var scope1 = manager.StartTransaction(doc, "Op1"))
                {
                    scope1.Commit();
                }

                using (var scope2 = manager.StartTransaction(doc, "Op2"))
                {
                    scope2.Commit();
                }

                manager.CommitGroup();

                if (manager.IsGroupActive)
                {
                    results.AppendLine("FAILED - Group should not be active after CommitGroup");
                    allPassed = false;
                }
                else
                {
                    results.AppendLine("PASSED");
                }
            }
        }
        catch (Exception ex)
        {
            manager.EnsureGroupClosed();
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 4: Transaction group rollback
        try
        {
            results.Append("Test 4: Transaction group rollback... ");

            manager.StartGroup(doc, "Test Group Rollback");

            using (var scope1 = manager.StartTransaction(doc, "Op1"))
            {
                scope1.Commit();
            }

            using (var scope2 = manager.StartTransaction(doc, "Op2"))
            {
                scope2.Commit();
            }

            manager.RollbackGroup();

            if (manager.IsGroupActive)
            {
                results.AppendLine("FAILED - Group should not be active after RollbackGroup");
                allPassed = false;
            }
            else
            {
                results.AppendLine("PASSED");
            }
        }
        catch (Exception ex)
        {
            manager.EnsureGroupClosed();
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 5: IsGroupActive tracking
        try
        {
            results.Append("Test 5: IsGroupActive tracking... ");

            if (manager.IsGroupActive)
            {
                results.AppendLine("FAILED - Group should not be active initially");
                allPassed = false;
            }
            else
            {
                manager.StartGroup(doc, "Test Active Tracking");

                if (!manager.IsGroupActive)
                {
                    results.AppendLine("FAILED - Group should be active after StartGroup");
                    allPassed = false;
                }
                else
                {
                    manager.CommitGroup();

                    if (manager.IsGroupActive)
                    {
                        results.AppendLine("FAILED - Group should not be active after CommitGroup");
                        allPassed = false;
                    }
                    else
                    {
                        results.AppendLine("PASSED");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            manager.EnsureGroupClosed();
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 6: EnsureGroupClosed (safe cleanup)
        try
        {
            results.Append("Test 6: EnsureGroupClosed safe cleanup... ");

            manager.StartGroup(doc, "Test EnsureGroupClosed");
            manager.EnsureGroupClosed();

            if (manager.IsGroupActive)
            {
                results.AppendLine("FAILED - Group should not be active after EnsureGroupClosed");
                allPassed = false;
            }
            else
            {
                // Call again - should not throw
                manager.EnsureGroupClosed();
                results.AppendLine("PASSED");
            }
        }
        catch (Exception ex)
        {
            manager.EnsureGroupClosed();
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        results.AppendLine();
        results.AppendLine("=========================");
        results.AppendLine(allPassed ? "All tests PASSED!" : "Some tests FAILED!");

        TaskDialog.Show("RevitAI Transaction Tests", results.ToString());

        return Result.Succeeded;
    }
}
