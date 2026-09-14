// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TaskBarHider;

internal sealed partial class TaskBarHiderPage : ListPage
{
    public TaskBarHiderPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Taskbar Hider";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        return [
            new ListItem(new ToggleTaskbarCommand())
        {
            Title = "Toggle Taskbar",
            Subtitle = "Hide or show the taskbar on all monitors",
        }
        ];
    }
}
