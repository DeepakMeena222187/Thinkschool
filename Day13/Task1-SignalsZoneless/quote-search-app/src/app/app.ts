import { Component } from '@angular/core';
import { LoginComponent } from './login/login.component';
import { QuoteSearchComponent } from './quote-search/quote-search.component';

@Component({
  selector: 'app-root',
  imports: [LoginComponent, QuoteSearchComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
