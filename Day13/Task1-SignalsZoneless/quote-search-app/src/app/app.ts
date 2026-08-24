import { Component } from '@angular/core';
import { LoginComponent } from './login/login.component';
import { QuoteSearchComponent } from './quote-search/quote-search.component';
import { QuoteListDetailComponent } from './quote-list-detail/quote-list-detail.component';

@Component({
  selector: 'app-root',
  imports: [LoginComponent, QuoteSearchComponent, QuoteListDetailComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
