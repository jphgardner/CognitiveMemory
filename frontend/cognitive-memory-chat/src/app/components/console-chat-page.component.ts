import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import type { ConsoleLayoutComponent } from '../console-layout.component';

@Component({
  selector: 'app-console-chat-page',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule],
  templateUrl: './console-chat-page.component.html',
})
export class ConsoleChatPageComponent {
  @Input({ required: true }) host!: ConsoleLayoutComponent;
}
