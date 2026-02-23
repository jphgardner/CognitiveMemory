import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ToolInvocationAudit } from '../models/console.models';

@Component({
  selector: 'app-console-overview-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './console-overview-page.component.html',
})
export class ConsoleOverviewPageComponent {
  @Input({ required: true }) turnsCount = 0;
  @Input({ required: true }) toolSuccessRateValue = 100;
  @Input({ required: true }) successfulToolCallsCount = 0;
  @Input({ required: true }) totalToolCallsCount = 0;
  @Input({ required: true }) averageResponseSecondsValue = 0;
  @Input({ required: true }) topMemoryLayerValue = 'none';
  @Input({ required: true }) activityPolylinePointsValue = '';
  @Input({ required: true }) activityAreaPointsValue = '';
  @Input({ required: true }) memoryLayerMetricsValue: Array<{ layer: string; count: number; percent: number }> = [];
  @Input({ required: true }) recentToolEventsValue: ToolInvocationAudit[] = [];
}
