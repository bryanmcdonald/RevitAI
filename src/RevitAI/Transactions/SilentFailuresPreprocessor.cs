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

using Autodesk.Revit.DB;

namespace RevitAI.Transactions;

/// <summary>
/// Preprocessor that silently handles transaction failures to prevent Revit from
/// showing modal dialogs that block the ExternalEvent thread indefinitely.
/// Warnings are deleted; errors are left for Revit to roll back automatically.
/// </summary>
public sealed class SilentFailuresPreprocessor : IFailuresPreprocessor
{
    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        var failures = failuresAccessor.GetFailureMessages();

        foreach (var failure in failures)
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteWarning(failure);
            }
        }

        return FailureProcessingResult.Continue;
    }
}
