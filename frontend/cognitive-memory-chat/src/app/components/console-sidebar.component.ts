import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ConsolePage, PageNavItem } from '../models/console.models';

@Component({
  selector: 'app-console-sidebar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './console-sidebar.component.html',
})
export class ConsoleSidebarComponent {
  @Input({ required: true }) navigation: PageNavItem[] = [];
  @Input({ required: true }) activePage: ConsolePage = 'overview';
  @Input({ required: true }) sessionId = '';
  @Input({ required: true }) assistantTurnCount = 0;
  @Input({ required: true }) toolSuccessRate = 100;
  @Input({ required: true }) averageResponseSeconds = 0;

  @Output() readonly pageSelected = new EventEmitter<ConsolePage>();

  select(page: ConsolePage): void {
    this.pageSelected.emit(page);
  }
}
