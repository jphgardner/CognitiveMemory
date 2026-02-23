import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StatusTone } from '../models/console.models';

@Component({
  selector: 'app-console-header',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './console-header.component.html',
})
export class ConsoleHeaderComponent implements OnChanges {
  @Input({ required: true }) activePageCaption = '';
  @Input({ required: true }) activePageLabel = '';
  @Input({ required: true }) statusTone: StatusTone = 'idle';
  @Input({ required: true }) statusText = 'Ready.';
  @Input({ required: true }) apiBaseUrl = '/api';
  @Input({ required: true }) sessionId = '';
  @Input({ required: true }) streaming = false;

  @Output() readonly apiBaseUrlChanged = new EventEmitter<string>();
  @Output() readonly sessionIdChanged = new EventEmitter<string>();
  @Output() readonly openClientPortal = new EventEmitter<void>();
  @Output() readonly openControlCenter = new EventEmitter<void>();

  apiBaseDraft = '/api';
  sessionIdDraft = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['apiBaseUrl']) {
      this.apiBaseDraft = this.apiBaseUrl;
    }

    if (changes['sessionId']) {
      this.sessionIdDraft = this.sessionId;
    }
  }

  statusClass(): Record<string, boolean> {
    return {
      'status-idle': this.statusTone === 'idle',
      'status-busy': this.statusTone === 'busy',
      'status-ok': this.statusTone === 'ok',
      'status-error': this.statusTone === 'error',
    };
  }

  emitApiBaseChange(): void {
    this.apiBaseUrlChanged.emit(this.apiBaseDraft);
  }

  emitSessionIdChange(): void {
    this.sessionIdChanged.emit(this.sessionIdDraft);
  }

  openPortal(): void {
    this.openClientPortal.emit();
  }

  openControls(): void {
    this.openControlCenter.emit();
  }
}
