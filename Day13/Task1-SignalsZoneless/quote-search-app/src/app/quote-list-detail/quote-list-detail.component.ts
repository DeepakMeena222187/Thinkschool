import { Component, signal } from '@angular/core';
import { QuoteListComponent } from './quote-list.component';
import { QuoteDetailComponent } from './quote-detail.component';

@Component({
  selector: 'app-quote-list-detail',
  imports: [QuoteListComponent, QuoteDetailComponent],
  templateUrl: './quote-list-detail.component.html',
  styleUrl: './quote-list-detail.component.css',
})
export class QuoteListDetailComponent {
  readonly selectedId = signal<number | null>(null);

  onQuoteSelected(id: number): void {
    this.selectedId.set(id);
  }
}
