import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-console-analytics-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './console-analytics-page.component.html',
})
export class ConsoleAnalyticsPageComponent {
  @Input({ required: true }) responseThroughputPerMinuteValue = 0;
  @Input({ required: true }) totalToolCallsCount = 0;
  @Input({ required: true }) successfulToolCallsCount = 0;
  @Input({ required: true }) toolOutcomeMetricsValue: Array<{ label: string; count: number; percent: number }> = [];
  @Input({ required: true }) activityPolylinePointsValue = '';
  @Input({ required: true }) activityAreaPointsValue = '';
  @Input({ required: true }) latestAssistantPreviewValue = '';
}
