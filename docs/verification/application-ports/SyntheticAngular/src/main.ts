import { Component } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';

@Component({
  selector: 'abox-root',
  template: '<h1>abox-issue44-real-angular-component</h1>',
})
class App {}

bootstrapApplication(App).catch(console.error);
