using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniauiForEasytier.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 管理可复用的 EasyTier 入口节点服务器地址簿。
/// </summary>
public partial class ServersView : UserControl
{
    private readonly ServerEndpointRepository? _repository;
    private List<ServerEndpoint> _endpoints = new();
    private ServerEndpoint? _editingEndpoint; // 当前编辑中的服务器；null 表示没有选中条目。

    /// <summary>初始化设计器使用的服务器列表视图。</summary>
    public ServersView() { InitializeComponent(); }

    /// <summary>
    /// 初始化服务器地址簿视图。
    /// </summary>
    /// <param name="repository">服务器地址仓储，类型为 ServerEndpointRepository，不可为空，必填。</param>
    public ServersView(ServerEndpointRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        AddServerButton.Click += AddServerButton_Click;
        SaveServerButton.Click += SaveServerButton_Click;
        CancelServerButton.Click += CancelServerButton_Click;
        RenderServers();
    }

    /// <summary>重建服务器表格和汇总文字。</summary>
    private void RenderServers()
    {
        if (_repository is null) return;
        _endpoints = _repository.GetAll().ToList();
        ServersSummaryText.Text = $"共 {_endpoints.Count} 个";

        ServerRowsPanel.Children.Clear();
        if (_endpoints.Count == 0)
        {
            var empty = new StackPanel
            {
                Spacing = 5,
                Margin = new Thickness(0, 34),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            empty.Children.Add(new TextBlock { Classes = { "empty-title" }, Text = "地址簿还没有服务器" });
            empty.Children.Add(new TextBlock { Classes = { "empty-caption" }, Text = "点击右上角“添加服务器”新增入口节点地址" });
            ServerRowsPanel.Children.Add(empty);
            return;
        }

        for (var index = 0; index < _endpoints.Count; index++)
        {
            ServerRowsPanel.Children.Add(CreateServerRow(_endpoints[index], index == _endpoints.Count - 1));
        }
    }

    /// <summary>
    /// 创建一行服务器地址表格。
    /// </summary>
    /// <param name="endpoint">服务器地址，类型为 ServerEndpoint，不可为空，必填。</param>
    /// <param name="isLastRow">是否为最后一行，类型为 bool；为真时不绘制底部分隔线。</param>
    /// <returns>表格行控件，类型为 Border。</returns>
    private Border CreateServerRow(ServerEndpoint endpoint, bool isLastRow)
    {
        var row = new Border
        {
            Classes = { "table-row" },
            MinHeight = 44,
            Padding = new Thickness(14, 8),
            BorderThickness = isLastRow ? new Thickness(0) : new Thickness(0, 0, 0, 1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(200, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(124, GridUnitType.Pixel)));

        var nameText = new TextBlock
        {
            Classes = { "table-cell" },
            Text = endpoint.Name,
            FontWeight = FontWeight.Medium
        };
        grid.Children.Add(nameText);

        // 节点地址使用等宽字体，便于逐字符核对协议、主机和端口。
        var addressText = new TextBlock
        {
            Classes = { "table-cell", "mono" },
            Text = endpoint.Address
        };
        Grid.SetColumn(addressText, 1);
        grid.Children.Add(addressText);

        var createdText = new TextBlock
        {
            Classes = { "table-cell-muted" },
            Text = endpoint.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        };
        Grid.SetColumn(createdText, 2);
        grid.Children.Add(createdText);

        var editButton = new Button
        {
            Content = "编辑",
            Tag = endpoint.Id,
            VerticalAlignment = VerticalAlignment.Center
        };
        editButton.Classes.Add("row-action");
        editButton.Classes.Add("outline");
        editButton.Click += ServerRowEditButton_Click;

        // 删除是不可逆操作，使用危险样式与编辑按钮区分。
        var deleteButton = new Button
        {
            Content = "删除",
            Tag = endpoint.Id,
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteButton.Classes.Add("row-action");
        deleteButton.Classes.Add("danger");
        deleteButton.Click += ServerRowDeleteButton_Click;

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionPanel.Children.Add(editButton);
        actionPanel.Children.Add(deleteButton);
        Grid.SetColumn(actionPanel, 3);
        grid.Children.Add(actionPanel);

        row.Child = grid;
        return row;
    }

    /// <summary>
    /// 进入新增服务器状态并清空编辑区。
    /// </summary>
    /// <param name="sender">触发事件的添加按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void AddServerButton_Click(object? sender, RoutedEventArgs e)
    {
        _editingEndpoint = new ServerEndpoint();
        ServerNameTextBox.Text = string.Empty;
        ServerAddressTextBox.Text = string.Empty;
        ServerEditorStatusText.Text = "正在新增服务器，填写名称和地址后保存";
    }

    /// <summary>
    /// 将表格行对应的服务器载入编辑区。
    /// </summary>
    /// <param name="sender">触发事件的编辑按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void ServerRowEditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long endpointId } || _repository is null) return;
        var endpoint = _endpoints.FirstOrDefault(item => item.Id == endpointId);
        if (endpoint is null) return;

        _editingEndpoint = endpoint;
        ServerNameTextBox.Text = endpoint.Name;
        ServerAddressTextBox.Text = endpoint.Address;
        ServerEditorStatusText.Text = $"正在编辑：{endpoint.Name}";
    }

    /// <summary>
    /// 删除表格行对应的服务器地址；已写入网络配置的地址文本不受影响。
    /// </summary>
    /// <param name="sender">触发事件的删除按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void ServerRowDeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long endpointId } || _repository is null) return;

        // 删除的条目正在编辑时同时清空编辑区。
        if (_editingEndpoint?.Id == endpointId)
        {
            ClearEditor();
        }

        _repository.Delete(endpointId);
        RenderServers();
    }

    /// <summary>
    /// 保存编辑区中的服务器地址。
    /// </summary>
    /// <param name="sender">触发事件的保存按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void SaveServerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null || _editingEndpoint is null) return;

        // 名称和地址都是必填字段，空白时提示且不落库。
        var name = ServerNameTextBox.Text?.Trim();
        var address = ServerAddressTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
        {
            ServerEditorStatusText.Text = "服务器名称和节点地址都不能为空";
            return;
        }

        _editingEndpoint.Name = name;
        _editingEndpoint.Address = address;
        _repository.Save(_editingEndpoint);
        RenderServers();
        ServerEditorStatusText.Text = "服务器已保存";
    }

    /// <summary>
    /// 取消编辑并恢复编辑区初始状态。
    /// </summary>
    /// <param name="sender">触发事件的取消按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void CancelServerButton_Click(object? sender, RoutedEventArgs e) => ClearEditor();

    /// <summary>清空编辑区并重置提示文字。</summary>
    private void ClearEditor()
    {
        _editingEndpoint = null;
        ServerNameTextBox.Text = string.Empty;
        ServerAddressTextBox.Text = string.Empty;
        ServerEditorStatusText.Text = "选择上方条目编辑，或点击“添加服务器”新增";
    }
}
